using Application.Interfaces;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class Repository<TEntity>(AppDbContext dbContext)
    : IRepository<TEntity>
    where TEntity : class
{
    private DbSet<TEntity> DbSet => dbContext.Set<TEntity>();

    public virtual async Task<TEntity?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await DbSet.FindAsync(
            [id],
            cancellationToken);
    }

    public virtual async Task AddAsync(
        TEntity entity,
        CancellationToken cancellationToken = default)
    {
        await DbSet.AddAsync(entity, cancellationToken);
    }

    public virtual void Update(TEntity entity)
    {
        DbSet.Update(entity);
    }

    public virtual void Remove(TEntity entity)
    {
        DbSet.Remove(entity);
    }
}