using System;
using System.Threading;
using System.Threading.Tasks;

namespace AssetsLibrarySystem.Avalonia.Services.BackendLauncher;

public interface IBackendLauncher : IAsyncDisposable
{
    Task StartAsync(CancellationToken ct = default);

    Task StopAsync(CancellationToken ct = default);

    bool IsRunning { get; }

    string BaseUrl { get; }
}
