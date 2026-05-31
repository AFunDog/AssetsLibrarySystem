using System;
using System.Threading;
using System.Threading.Tasks;

namespace AssetsLibrarySystem.Avalonia.Services.BackendLauncher;

/// <summary>
/// Python 后端进程生命周期管理。
/// </summary>
public interface IBackendLauncher : IAsyncDisposable
{
    /// <summary>
    /// 启动后端进程并等待健康检查通过。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// 停止后端进程（优雅关闭，超时后强杀）。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// 后端是否正在运行。
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 后端服务的基地址，例如 http://127.0.0.1:8000。
    /// </summary>
    string BaseUrl { get; }
}
