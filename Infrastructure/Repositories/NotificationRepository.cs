using Application.Interfaces;
using Domain;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class NotificationRepository(AppDbContext dbContext)
    : Repository<Notification>(dbContext), INotificationRepository
{
    private readonly AppDbContext _dbContext = dbContext;

    public async Task<IReadOnlyList<Notification>> GetQueuedAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Notification>()
            .Where(n => n.Status == NotificationStatus.Queued)
            .OrderBy(n => n.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}