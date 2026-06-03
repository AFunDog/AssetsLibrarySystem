using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.Services.Infrastructure;

public sealed class DatabaseWriteQueue : IDatabaseWriteQueue
{
    private readonly Channel<IQueuedWrite> _queue = Channel.CreateUnbounded<IQueuedWrite>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });

    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;
    private bool _disposed;

    public DatabaseWriteQueue()
    {
        _worker = Task.Run(ProcessQueueAsync);
    }

    public ValueTask EnqueueAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)
    {
        return new ValueTask(EnqueueAsync(async token =>
        {
            await work(token).ConfigureAwait(false);
            return true;
        }, cancellationToken).AsTask());
    }

    public ValueTask<T> EnqueueAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DatabaseWriteQueue));
        }

        var item = new QueuedWrite<T>(work, cancellationToken);
        if (!_queue.Writer.TryWrite(item))
        {
            throw new InvalidOperationException("数据库写队列不可用。");
        }

        return new ValueTask<T>(item.Task);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.Writer.TryComplete();
        _shutdown.Cancel();

        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "数据库写队列在关闭时出现异常。");
        }
        finally
        {
            _shutdown.Dispose();
        }
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                await item.ExecuteAsync(_shutdown.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private interface IQueuedWrite
    {
        Task ExecuteAsync(CancellationToken shutdownToken);
    }

    private sealed class QueuedWrite<T> : IQueuedWrite
    {
        private readonly Func<CancellationToken, Task<T>> _work;
        private readonly CancellationToken _callerToken;
        private readonly TaskCompletionSource<T> _taskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public QueuedWrite(Func<CancellationToken, Task<T>> work, CancellationToken callerToken)
        {
            _work = work;
            _callerToken = callerToken;

            if (_callerToken.CanBeCanceled)
            {
                _callerToken.Register(() => _taskCompletionSource.TrySetCanceled(_callerToken));
            }
        }

        public Task<T> Task => _taskCompletionSource.Task;

        public async Task ExecuteAsync(CancellationToken shutdownToken)
        {
            if (_taskCompletionSource.Task.IsCompleted)
            {
                return;
            }

            if (_callerToken.IsCancellationRequested)
            {
                _taskCompletionSource.TrySetCanceled(_callerToken);
                return;
            }

            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken, _callerToken);
                var result = await _work(linkedCts.Token).ConfigureAwait(false);
                _taskCompletionSource.TrySetResult(result);
            }
            catch (OperationCanceledException oce) when (_callerToken.IsCancellationRequested)
            {
                _taskCompletionSource.TrySetCanceled(oce.CancellationToken);
            }
            catch (Exception ex)
            {
                _taskCompletionSource.TrySetException(ex);
            }
        }
    }
}
