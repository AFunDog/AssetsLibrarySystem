using System;
using System.Threading;
using System.Threading.Tasks;

namespace AssetsLibrarySystem.Avalonia.Services.Infrastructure;

public interface IDatabaseWriteQueue : IAsyncDisposable, IDisposable
{
    ValueTask EnqueueAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default);

    ValueTask<T> EnqueueAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default);
}
