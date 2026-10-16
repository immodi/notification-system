using Domain;

namespace Application.Interfaces;

public interface INotificationService
{
    Task<Guid> CreateAsync(
        string recipient,
        string subject,
        string message,
        CancellationToken cancellationToken = default);

    Task<Notification?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Notification>> GetQueuedAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Notification notification,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}