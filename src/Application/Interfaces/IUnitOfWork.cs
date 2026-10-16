using Domain;

namespace Application.Interfaces;

public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}