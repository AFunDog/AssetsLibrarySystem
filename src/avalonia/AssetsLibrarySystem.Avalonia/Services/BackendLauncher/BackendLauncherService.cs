using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace AssetsLibrarySystem.Avalonia.Services.BackendLauncher;

/// <summary>
/// 通过命令行启动 Python 后端（uvicorn），并轮询 /health 等待就绪。
/// </summary>
public sealed class BackendLauncherService : IBackendLauncher
{
    private readonly BackendLauncherOptions _options;
    private Process? _process;
    private readonly HttpClient _http = new();

    public BackendLauncherService(IConfiguration configuration)
    {
        _options = BuildOptions(configuration);
        BaseUrl = $"http://{_options.Host}:{_options.Port}";
    }

    public bool IsRunning => _process is { HasExited: false };
    public string BaseUrl { get; }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning)
            return;

        var psi = CreateProcessStartInfo();

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 Python 进程。");

        // 后台吞掉 stdout/stderr 防止管道阻塞
        _ = _process.StandardOutput.ReadToEndAsync(ct);
        _ = _process.StandardError.ReadToEndAsync(ct);

        await WaitForHealthAsync(ct);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_process is not { HasExited: false })
            return;

        _process.Kill(entireProcessTree: true);

        // 等待进程退出，最多 5 秒
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            await _process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            // 超时，忽略
        }

        _process.Dispose();
        _process = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _http.Dispose();
    }

    /// <summary>
    /// 轮询 /health 直到返回 200，或超时。
    /// </summary>
    private async Task WaitForHealthAsync(CancellationToken ct)
    {
        var healthUrl = $"{BaseUrl}/health";
        var deadline = DateTime.UtcNow + _options.StartupTimeout;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (_process?.HasExited == true)
                throw new InvalidOperationException(
                    $"Python 后端进程已退出（exit code: {_process.ExitCode}）。");

            try
            {
                var resp = await _http.GetAsync(healthUrl, ct);
                if (resp.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
                // 服务还没起来，继续等
            }

            await Task.Delay(_options.HealthCheckInterval, ct);
        }

        throw new TimeoutException(
            $"后端健康检查超时（{_options.StartupTimeout.TotalSeconds}s），地址: {healthUrl}");
    }

    private ProcessStartInfo CreateProcessStartInfo()
    {
#if DEBUG
        var fileName = ResolvePath(_options.DebugPythonPath);
        var arguments = FormatArguments(_options.DebugArgumentsTemplate);
#else
        var fileName = ResolvePath(_options.PublishedExecutablePath);
        var arguments = FormatArguments(_options.PublishedArgumentsTemplate);
#endif

        return new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = _options.BackendWorkingDirectory,
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

        return Path.GetFullPath(Path.Combine(_options.BackendWorkingDirectory, configuredPath));
    }

    private string FormatArguments(string template)
    {
        return template
            .Replace("{host}", _options.Host, StringComparison.OrdinalIgnoreCase)
            .Replace("{port}", _options.Port.ToString(), StringComparison.OrdinalIgnoreCase);
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
