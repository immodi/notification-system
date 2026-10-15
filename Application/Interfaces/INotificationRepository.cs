using Domain;

namespace Application.Interfaces;

public interface INotificationRepository
    : IRepository<Notification>
{
    Task<IReadOnlyList<Notification>> GetQueuedAsync(
        int limit,
        CancellationToken cancellationToken = default);
}