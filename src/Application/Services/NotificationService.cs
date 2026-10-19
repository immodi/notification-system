using Application.DTOs;
using Application.Interfaces;
using Domain;

namespace Application.Services;

public sealed class NotificationService(
    IUnitOfWork unitOfWork,
    INotificationRepository repository,
    IQueue<Notification> queue)
    : INotificationService
{
    public async Task<Guid> CreateAsync(
        NotificationDto dto,
        CancellationToken cancellationToken = default)
    {
        var notification = new Notification(
            dto.Recipient,
            dto.Subject,
            dto.Message);

        await repository.AddAsync(
            notification,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await queue.EnqueueAsync(
            notification,
            cancellationToken);

        return notification.Id;
    }

    public async Task<Notification?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await repository.GetByIdAsync(
            id,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Notification>> GetQueuedAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return await repository.GetQueuedAsync(
            limit,
            cancellationToken);
    }

    public async Task UpdateAsync(
        Notification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        repository.Update(notification);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var notification = await repository.GetByIdAsync(
            id,
            cancellationToken);

        if (notification is null)
            return;

        repository.Remove(notification);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}