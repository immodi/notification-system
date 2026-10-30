namespace Application.Interfaces;

public interface IQueueMessage<out T> : IAsyncDisposable
{
    T Payload { get; }

    Task AcknowledgeAsync(
        CancellationToken cancellationToken = default);

    Task RejectAsync(
        bool requeue,
        CancellationToken cancellationToken = default);
}
