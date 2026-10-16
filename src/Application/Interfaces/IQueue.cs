namespace Application.Interfaces;

public interface IQueue<T>
{
    Task EnqueueAsync(
        T message,
        CancellationToken cancellationToken = default);

    Task<T> DequeueAsync(
        CancellationToken cancellationToken = default);
}