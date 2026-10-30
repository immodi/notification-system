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

    public async Task<IQueueMessage<T>?> DequeueAsync(
        CancellationToken cancellationToken = default)
    {
        await _signal.WaitAsync(cancellationToken);

        lock (_queue)
        {
            return new InMemoryQueueMessage(_queue.Dequeue());
        }
    }

    public Task EnqueueDelayedAsync(
        T message,
        TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (delay <= TimeSpan.Zero)
            return EnqueueAsync(message, cancellationToken);

        _ = DeliverLaterAsync(message, delay);

        return Task.CompletedTask;
    }

    private async Task DeliverLaterAsync(T message, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay);
            await EnqueueAsync(message);
        }
        catch
        {
            // The in-memory queue is best effort; the RabbitMQ queue owns
            // durability in production.
        }
    }

    private sealed class InMemoryQueueMessage(T payload) : IQueueMessage<T>
    {
        public T Payload { get; } = payload;

        public Task AcknowledgeAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RejectAsync(
            bool requeue,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
