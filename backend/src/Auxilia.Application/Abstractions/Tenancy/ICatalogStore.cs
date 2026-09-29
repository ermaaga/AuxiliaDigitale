using Auxilia.Domain.Platform;

namespace Auxilia.Application.Abstractions.Tenancy;

/// <summary>Write access to the Catalog aggregates for the Platform module (tracked entities, one unit of work).</summary>
public interface ICatalogStore
{
    Task<Tenant?> FindTenantAsync(string slug, CancellationToken cancellationToken);

    Task<IReadOnlyList<Tenant>> ListTenantsAsync(CancellationToken cancellationToken);

    /// <summary>The plan assigned to new tenants (<c>standard</c>).</summary>
    Task<Guid> DefaultPlanIdAsync(CancellationToken cancellationToken);

    void Add(Tenant tenant);

    void Add(TenantPlan tenantPlan);

    void Add(MigrationRun run);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Applies the Catalog schema migrations (auxctl only; never at API/Worker startup).</summary>
public interface ICatalogMigrator
{
    /// <summary>Returns the migrations applied now.</summary>
    Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken);
}
