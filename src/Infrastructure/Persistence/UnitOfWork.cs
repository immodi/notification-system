using Application.Interfaces;
using Domain;
using Infrastructure.Repositories;

namespace Infrastructure.Persistence;

public sealed class UnitOfWork(
    AppDbContext dbContext
    ) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    public void Dispose()
    {
        dbContext.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        return dbContext.DisposeAsync();
    }
}