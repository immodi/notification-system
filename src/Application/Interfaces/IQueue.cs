namespace Application.Interfaces;

public interface IQueue<T>
{
    Task EnqueueAsync(
        T message,
        CancellationToken cancellationToken = default);

    Task EnqueueDelayedAsync(
        T message,
        TimeSpan delay,
        CancellationToken cancellationToken = default);

    Task<IQueueMessage<T>?> DequeueAsync(
        CancellationToken cancellationToken = default);
}
