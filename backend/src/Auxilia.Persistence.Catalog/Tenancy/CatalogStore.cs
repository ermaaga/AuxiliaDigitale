using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Domain.Platform;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Catalog.Tenancy;

/// <inheritdoc cref="ICatalogStore"/>
internal sealed class CatalogStore : ICatalogStore
{
    private readonly CatalogDbContext catalog;

    public CatalogStore(CatalogDbContext catalog) => this.catalog = catalog;

    public Task<Tenant?> FindTenantAsync(string slug, CancellationToken cancellationToken) =>
        catalog.Tenants.SingleOrDefaultAsync(tenant => tenant.Slug == slug, cancellationToken);

    public async Task<IReadOnlyList<Tenant>> ListTenantsAsync(CancellationToken cancellationToken) =>
        await catalog.Tenants.OrderBy(tenant => tenant.Slug).ToListAsync(cancellationToken);

    public Task<Guid> DefaultPlanIdAsync(CancellationToken cancellationToken) =>
        catalog.Plans.Where(plan => plan.IsDefault && plan.IsActive).Select(plan => plan.Id).SingleAsync(cancellationToken);

    public void Add(Tenant tenant) => catalog.Tenants.Add(tenant);

    public void Add(TenantPlan tenantPlan) => catalog.TenantPlans.Add(tenantPlan);

    public void Add(MigrationRun run) => catalog.MigrationRuns.Add(run);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => catalog.SaveChangesAsync(cancellationToken);
}

/// <inheritdoc cref="ICatalogMigrator"/>
internal sealed class CatalogMigrator : ICatalogMigrator
{
    private readonly CatalogDbContext catalog;

    public CatalogMigrator(CatalogDbContext catalog) => this.catalog = catalog;

    public async Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken)
    {
        var pending = (await catalog.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        await catalog.Database.MigrateAsync(cancellationToken);
        return pending;
    }
}
