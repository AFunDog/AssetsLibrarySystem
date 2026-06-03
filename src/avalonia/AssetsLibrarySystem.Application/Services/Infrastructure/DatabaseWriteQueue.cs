using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Serilog;

namespace AssetsLibrarySystem.Application.Services.Infrastructure;

public sealed class DatabaseWriteQueue : IDatabaseWriteQueue
{
    private readonly Channel<IQueuedWrite> _queue = Channel.CreateUnbounded<IQueuedWrite>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });

    private readonly Task _worker;
    private int _disposed;

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
        if (Volatile.Read(ref _disposed) != 0)
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
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _queue.Writer.TryComplete();

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
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                await item.ExecuteAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private interface IQueuedWrite
    {
        Task ExecuteAsync();
    }

    private sealed class QueuedWrite<T> : IQueuedWrite
    {
        private readonly Func<CancellationToken, Task<T>> _work;
        private readonly CancellationToken _callerToken;
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private readonly TaskCompletionSource<T> _taskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public QueuedWrite(Func<CancellationToken, Task<T>> work, CancellationToken callerToken)
        {
            _work = work;
            _callerToken = callerToken;

            if (_callerToken.CanBeCanceled)
            {
                _cancellationRegistration = _callerToken.Register(() => _taskCompletionSource.TrySetCanceled(_callerToken));
            }
        }

        public Task<T> Task => _taskCompletionSource.Task;

        public async Task ExecuteAsync()
        {
            try
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

                var result = await _work(_callerToken).ConfigureAwait(false);
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
            finally
            {
                _cancellationRegistration.Dispose();
            }
        }
    }
}
