using Application.Interfaces;

namespace Infrastructure.Messaging;

public class InMemoryQueue<T> : IQueue<T>
{
    private readonly Queue<T> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);

    public Task EnqueueAsync(
        T message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_queue)
        {
            _queue.Enqueue(message);
        }

        _signal.Release();

        return Task.CompletedTask;
    }

    public async Task<T> DequeueAsync(
        CancellationToken cancellationToken = default)
    {
        await _signal.WaitAsync(cancellationToken);

        lock (_queue)
        {
            return _queue.Dequeue();
        }
    }
}