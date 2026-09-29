using Microsoft.EntityFrameworkCore;

namespace Auxilia.Application.Abstractions.Persistence;

/// <summary>
/// The database of the current tenant, as seen by Managers and QueryServices. Obtained only from
/// <see cref="ITenantDbContextFactory"/>; one instance per operation, disposed by the caller.
/// </summary>
public interface ITenantDbContext : IAsyncDisposable
{
    DbSet<TEntity> Set<TEntity>()
        where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Creates a context on the database of the tenant in <c>ITenantContext</c> (per-tenant connection pool).</summary>
public interface ITenantDbContextFactory
{
    Task<ITenantDbContext> CreateAsync(CancellationToken cancellationToken);
}
