using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Infrastructure;
using AssetsLibrarySystem.Application.Services.BackendApi;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace AssetsLibrarySystem.Application.Services.BackendLauncher;

public sealed class BackendLauncherService : IBackendLauncher
{
    private int StopCompleted;
    private BackendLauncherOptions Options { get; }
    private Process? BackendProcess { get; set; }
    private IBackendHealthClient BackendHealthClient { get; }
    private CancellationTokenSource? HeartbeatCts { get; set; }

    public BackendLauncherService(IConfiguration configuration, IBackendHealthClient backendHealthClient)
    {
        Options = BuildOptions(configuration);
        BackendHealthClient = backendHealthClient;
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
            "准备启动后端: mode={Mode}, file={File}, arguments={Arguments}, workingDirectory={WorkingDirectory}, launcherPid={LauncherPid}",
            IsDebugBuild ? "Debug" : "Release",
            psi.FileName,
            psi.Arguments,
            psi.WorkingDirectory,
            Environment.ProcessId);

        BackendProcess = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 Python 进程。");

        Log.Information(
            "后端进程已启动，pid={BackendPid}, backendExecutable={BackendExecutable}, launcherPid={LauncherPid}",
            BackendProcess.Id,
            psi.FileName,
            Environment.ProcessId);

        await WaitForHealthAsync(ct);
        StartHeartbeatLoop();
        Log.Information("后端健康检查通过，baseUrl={BaseUrl}", BaseUrl);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref StopCompleted, 1) == 1)
        {
            return;
        }

        StopHeartbeatLoop();

        if (BackendProcess is not { HasExited: false })
        {
            Log.Information("后端进程未运行，跳过停止");
            return;
        }

        Log.Information("准备停止后端进程，pid={Pid}", BackendProcess.Id);
        BackendProcess.Kill(entireProcessTree: true);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            await BackendProcess.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
        }

        BackendProcess.Dispose();
        BackendProcess = null;
        Log.Information("后端进程已停止");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private async Task WaitForHealthAsync(CancellationToken ct)
    {
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
                if (await BackendHealthClient.IsHealthyAsync(BaseUrl, ct).ConfigureAwait(false))
                    return;
            }
            catch (TaskCanceledException)
            {
                // 单次健康检查超时不代表后端启动失败，继续等到总超时。
            }

            await Task.Delay(Options.HealthCheckInterval, ct);
        }

        Log.Error("后端健康检查超时，baseUrl={BaseUrl}, timeoutSeconds={TimeoutSeconds}", BaseUrl, Options.StartupTimeout.TotalSeconds);
        throw new TimeoutException(
            $"后端健康检查超时（{Options.StartupTimeout.TotalSeconds}s），地址: {BaseUrl}");
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

        var processStartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = Options.BackendWorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        processStartInfo.Environment["BACKEND_HEARTBEAT_TIMEOUT"] = Options.HeartbeatTimeout.TotalSeconds.ToString("0.###");
        processStartInfo.Environment["BACKEND_HEARTBEAT_CHECK_INTERVAL"] = "1";
        processStartInfo.Environment["BACKEND_HEARTBEAT_STARTUP_GRACE"] = Options.HeartbeatStartupGrace.TotalSeconds.ToString("0.###");
        processStartInfo.Environment["BACKEND_LAUNCHER_PID"] = Environment.ProcessId.ToString();
        processStartInfo.Environment["APP_ENV"] = IsDebugBuild ? "dev" : "prod";
        processStartInfo.Environment["DATA_ROOT"] = Options.DataRoot;

        return processStartInfo;
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

    private void StartHeartbeatLoop()
    {
        StopHeartbeatLoop();
        var heartbeatCts = new CancellationTokenSource();
        HeartbeatCts = heartbeatCts;

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(Options.HeartbeatInterval);
            while (await timer.WaitForNextTickAsync(heartbeatCts.Token))
            {
                try
                {
                    await BackendHealthClient.SendHeartbeatAsync(BaseUrl, heartbeatCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "后端心跳发送失败");
                }
            }
        }, HeartbeatCts.Token);
    }

    private void StopHeartbeatLoop()
    {
        HeartbeatCts?.Cancel();
        HeartbeatCts?.Dispose();
        HeartbeatCts = null;
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
            backendWorkingDirectory = RuntimePathHelper.ResolveBackendWorkingDirectory();
        }
        else
        {
            backendWorkingDirectory = Path.GetFullPath(backendWorkingDirectory);
        }

        var dataRoot = configuration["Runtime:DataRoot"];
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            dataRoot = RuntimePathHelper.ResolveDataRoot();
        }
        else
        {
            dataRoot = Path.GetFullPath(dataRoot);
        }

        return new BackendLauncherOptions
        {
            DebugPythonPath = section["DebugPythonPath"] ?? @".venv\Scripts\python.exe",
            DebugArgumentsTemplate = section["DebugArgumentsTemplate"] ?? "-m uvicorn app.main:app --host {host} --port {port}",
            PublishedExecutablePath = section["PublishedExecutablePath"] ?? "assets-library-system-backend.exe",
            PublishedArgumentsTemplate = section["PublishedArgumentsTemplate"] ?? "--host {host} --port {port}",
            BackendWorkingDirectory = backendWorkingDirectory,
            DataRoot = dataRoot,
            Host = section["Host"] ?? "127.0.0.1",
            Port = section.GetValue<int?>("Port") ?? 8000,
            StartupTimeout = TimeSpan.FromSeconds(section.GetValue<double?>("StartupTimeoutSeconds") ?? 30),
            HealthCheckInterval = TimeSpan.FromMilliseconds(section.GetValue<double?>("HealthCheckIntervalMilliseconds") ?? 500),
            HeartbeatInterval = TimeSpan.FromSeconds(section.GetValue<double?>("HeartbeatIntervalSeconds") ?? 2),
            HeartbeatTimeout = TimeSpan.FromSeconds(section.GetValue<double?>("HeartbeatTimeoutSeconds") ?? 8),
            HeartbeatStartupGrace = TimeSpan.FromSeconds(section.GetValue<double?>("HeartbeatStartupGraceSeconds") ?? 15),
        };
    }
}
