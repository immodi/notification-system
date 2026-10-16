namespace Application.Interfaces;

public interface IRepository<TEntity>
    where TEntity : class
{
    Task<TEntity?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        TEntity entity,
        CancellationToken cancellationToken = default);

    void Update(TEntity entity);

    void Remove(TEntity entity);
}