using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.Services.BackendLauncher;

/// <summary>
/// 通过命令行启动 Python 后端（uvicorn），并轮询 /health 等待就绪。
/// </summary>
public sealed class BackendLauncherService : IBackendLauncher
{
    private BackendLauncherOptions Options { get; }
    private Process? BackendProcess { get; set; }
    private HttpClient Http { get; } = new();

    public BackendLauncherService(IConfiguration configuration)
    {
        Options = BuildOptions(configuration);
        BaseUrl = $"http://{Options.Host}:{Options.Port}";
    }

    public bool IsRunning => BackendProcess is { HasExited: false };
    public string BaseUrl { get; }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning)
        {
            Log.Information("后端进程已在运行，跳过重复启动");
            return;
        }

        var psi = CreateProcessStartInfo();
        Log.Information(
            "准备启动后端: mode={Mode}, file={File}, arguments={Arguments}, workingDirectory={WorkingDirectory}",
            IsDebugBuild ? "Debug" : "Release",
            psi.FileName,
            psi.Arguments,
            psi.WorkingDirectory);

        BackendProcess = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 Python 进程。");

        Log.Information("后端进程已启动，pid={Pid}", BackendProcess.Id);

        // 后台吞掉 stdout/stderr 防止管道阻塞
        _ = BackendProcess.StandardOutput.ReadToEndAsync(ct);
        _ = BackendProcess.StandardError.ReadToEndAsync(ct);

        await WaitForHealthAsync(ct);
        Log.Information("后端健康检查通过，baseUrl={BaseUrl}", BaseUrl);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (BackendProcess is not { HasExited: false })
        {
            Log.Information("后端进程未运行，跳过停止");
            return;
        }

        Log.Information("准备停止后端进程，pid={Pid}", BackendProcess.Id);
        BackendProcess.Kill(entireProcessTree: true);

        // 等待进程退出，最多 5 秒
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            await BackendProcess.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            // 超时，忽略
        }

        BackendProcess.Dispose();
        BackendProcess = null;
        Log.Information("后端进程已停止");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        Http.Dispose();
    }

    /// <summary>
    /// 轮询 /health 直到返回 200，或超时。
    /// </summary>
    private async Task WaitForHealthAsync(CancellationToken ct)
    {
        var healthUrl = $"{BaseUrl}/health";
        var deadline = DateTime.UtcNow + Options.StartupTimeout;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (BackendProcess?.HasExited == true)
            {
                Log.Error("后端进程提前退出，exitCode={ExitCode}", BackendProcess.ExitCode);
                throw new InvalidOperationException(
                    $"Python 后端进程已退出（exit code: {BackendProcess.ExitCode}）。");
            }

            try
            {
                var resp = await Http.GetAsync(healthUrl, ct);
                if (resp.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
                // 服务还没起来，继续等
            }

            await Task.Delay(Options.HealthCheckInterval, ct);
        }

        Log.Error("后端健康检查超时，url={HealthUrl}, timeoutSeconds={TimeoutSeconds}", healthUrl, Options.StartupTimeout.TotalSeconds);
        throw new TimeoutException(
            $"后端健康检查超时（{Options.StartupTimeout.TotalSeconds}s），地址: {healthUrl}");
    }

    private ProcessStartInfo CreateProcessStartInfo()
    {
#if DEBUG
        var fileName = ResolvePath(Options.DebugPythonPath);
        var arguments = FormatArguments(Options.DebugArgumentsTemplate);
#else
        var fileName = ResolvePath(Options.PublishedExecutablePath);
        var arguments = FormatArguments(Options.PublishedArgumentsTemplate);
#endif

        return new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = Options.BackendWorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
    }

    private string ResolvePath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException("后端启动路径不能为空。");
        }

        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        return Path.GetFullPath(Path.Combine(Options.BackendWorkingDirectory, configuredPath));
    }

    private string FormatArguments(string template)
    {
        return template
            .Replace("{host}", Options.Host, StringComparison.OrdinalIgnoreCase)
            .Replace("{port}", Options.Port.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDebugBuild
    {
#if DEBUG
        get => true;
#else
        get => false;
#endif
    }

    private static BackendLauncherOptions BuildOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("BackendLauncher");
        var backendWorkingDirectory = section["BackendWorkingDirectory"];
        if (string.IsNullOrWhiteSpace(backendWorkingDirectory))
        {
            throw new InvalidOperationException("缺少配置项 BackendLauncher:BackendWorkingDirectory。");
        }

        return new BackendLauncherOptions
        {
            DebugPythonPath = section["DebugPythonPath"] ?? @".venv\Scripts\python.exe",
            DebugArgumentsTemplate = section["DebugArgumentsTemplate"] ?? "-m uvicorn app.main:app --host {host} --port {port}",
            PublishedExecutablePath = section["PublishedExecutablePath"] ?? "assets-library-system-backend.exe",
            PublishedArgumentsTemplate = section["PublishedArgumentsTemplate"] ?? "--host {host} --port {port}",
            BackendWorkingDirectory = backendWorkingDirectory,
            Host = section["Host"] ?? "127.0.0.1",
            Port = section.GetValue<int?>("Port") ?? 8000,
            StartupTimeout = TimeSpan.FromSeconds(section.GetValue<double?>("StartupTimeoutSeconds") ?? 30),
            HealthCheckInterval = TimeSpan.FromMilliseconds(section.GetValue<double?>("HealthCheckIntervalMilliseconds") ?? 500),
        };
    }
}
