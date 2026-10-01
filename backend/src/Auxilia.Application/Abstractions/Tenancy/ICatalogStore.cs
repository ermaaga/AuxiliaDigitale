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

    /// <summary>Every module row, available or not (tracked).</summary>
    Task<IReadOnlyList<PlatformModule>> ListModulesAsync(CancellationToken cancellationToken);

    void Add(PlatformModule module);

    /// <summary>A plan with its modules (tracked).</summary>
    Task<Plan?> FindPlanAsync(Guid planId, CancellationToken cancellationToken);

    /// <summary>Every plan with its modules (tracked).</summary>
    Task<IReadOnlyList<Plan>> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>The plan period of the tenant valid at <paramref name="at"/> (tracked), if any.</summary>
    Task<TenantPlan?> CurrentTenantPlanAsync(Guid tenantId, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Code of the plan valid at <paramref name="at"/> for every tenant that has one (read-only).</summary>
    Task<IReadOnlyDictionary<Guid, string>> CurrentPlanCodesAsync(DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Module overrides of the tenant (tracked).</summary>
    Task<IReadOnlyList<TenantModuleOverride>> ListOverridesAsync(Guid tenantId, CancellationToken cancellationToken);

    void Add(TenantModuleOverride moduleOverride);

    void Remove(TenantModuleOverride moduleOverride);

    /// <summary>The latest runs (provisioning, migrations, jobs) of the tenant, newest first (read-only).</summary>
    Task<IReadOnlyList<MigrationRun>> RecentRunsAsync(Guid tenantId, int count, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Applies the Catalog schema migrations (auxctl only; never at API/Worker startup).</summary>
public interface ICatalogMigrator
{
    /// <summary>Returns the migrations applied now.</summary>
    Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken);
}
