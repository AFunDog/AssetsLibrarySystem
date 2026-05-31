using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AssetsLibrarySystem.Avalonia.Services.BackendLauncher;

/// <summary>
/// 通过命令行启动 Python 后端（uvicorn），并轮询 /health 等待就绪。
/// </summary>
public sealed class BackendLauncherService : IBackendLauncher
{
    private readonly BackendLauncherOptions _options;
    private Process? _process;
    private readonly HttpClient _http = new();

    public BackendLauncherService(BackendLauncherOptions options)
    {
        _options = options;
        BaseUrl = $"http://{options.Host}:{options.Port}";
    }

    public bool IsRunning => _process is { HasExited: false };
    public string BaseUrl { get; }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning)
            return;

        var arguments = $"-m uvicorn app.main:app " +
                        $"--host {_options.Host} --port {_options.Port}";

        var psi = new ProcessStartInfo
        {
            FileName = _options.PythonPath,
            Arguments = arguments,
            WorkingDirectory = _options.BackendWorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

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
}
