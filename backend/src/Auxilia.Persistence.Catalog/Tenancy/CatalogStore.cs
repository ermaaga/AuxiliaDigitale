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

    public async Task<IReadOnlyList<PlatformModule>> ListModulesAsync(CancellationToken cancellationToken) =>
        await catalog.Modules.ToListAsync(cancellationToken);

    public void Add(PlatformModule module) => catalog.Modules.Add(module);

    public Task<Plan?> FindPlanAsync(Guid planId, CancellationToken cancellationToken) =>
        catalog.Plans.Include(plan => plan.Modules).SingleOrDefaultAsync(plan => plan.Id == planId, cancellationToken);

    public async Task<IReadOnlyList<Plan>> ListPlansAsync(CancellationToken cancellationToken) =>
        await catalog.Plans.Include(plan => plan.Modules).OrderBy(plan => plan.Code).ToListAsync(cancellationToken);

    public Task<TenantPlan?> CurrentTenantPlanAsync(Guid tenantId, DateTimeOffset at, CancellationToken cancellationToken) =>
        catalog.TenantPlans
            .Where(period => period.TenantId == tenantId && period.ValidFrom <= at && (period.ValidTo == null || period.ValidTo > at))
            .OrderByDescending(period => period.ValidFrom)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> CurrentPlanCodesAsync(DateTimeOffset at, CancellationToken cancellationToken)
    {
        var periods = await catalog.TenantPlans.AsNoTracking()
            .Where(period => period.ValidFrom <= at && (period.ValidTo == null || period.ValidTo > at))
            .Join(catalog.Plans, period => period.PlanId, plan => plan.Id, (period, plan) => new { period.TenantId, period.ValidFrom, plan.Code })
            .ToListAsync(cancellationToken);
        return periods
            .GroupBy(period => period.TenantId)
            .ToDictionary(group => group.Key, group => group.MaxBy(period => period.ValidFrom)!.Code);
    }

    public async Task<IReadOnlyList<TenantModuleOverride>> ListOverridesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await catalog.TenantModuleOverrides.Where(item => item.TenantId == tenantId).ToListAsync(cancellationToken);

    public void Add(TenantModuleOverride moduleOverride) => catalog.TenantModuleOverrides.Add(moduleOverride);

    public void Remove(TenantModuleOverride moduleOverride) => catalog.TenantModuleOverrides.Remove(moduleOverride);

    public async Task<IReadOnlyList<MigrationRun>> RecentRunsAsync(Guid tenantId, int count, CancellationToken cancellationToken) =>
        await catalog.MigrationRuns.AsNoTracking()
            .Where(run => run.TenantId == tenantId)
            .OrderByDescending(run => run.StartedAt)
            .Take(count)
            .ToListAsync(cancellationToken);

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
