using Auxilia.Application.Abstractions.Modules;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Catalog.Modules;

internal sealed class ModuleCatalogReader : IModuleCatalogReader
{
    private readonly CatalogDbContext catalog;

    public ModuleCatalogReader(CatalogDbContext catalog) => this.catalog = catalog;

    public async Task<TenantModuleSource> GetSourceAsync(Guid tenantId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var modules = await catalog.Modules.AsNoTracking()
            .Where(module => module.IsAvailable)
            .Select(module => new CatalogModule(module.Id, module.Kind))
            .ToListAsync(cancellationToken);

        // The plan valid now; if validity periods overlap, the one that started last wins.
        var planId = await catalog.TenantPlans.AsNoTracking()
            .Where(tenantPlan => tenantPlan.TenantId == tenantId && tenantPlan.ValidFrom <= at && (tenantPlan.ValidTo == null || at < tenantPlan.ValidTo))
            .OrderByDescending(tenantPlan => tenantPlan.ValidFrom)
            .Select(tenantPlan => (Guid?)tenantPlan.PlanId)
            .FirstOrDefaultAsync(cancellationToken);

        var planModules = planId is null
            ? new Dictionary<string, TenantRole[]>()
            : (await catalog.Plans.AsNoTracking()
                .Where(plan => plan.Id == planId && plan.IsActive)
                .SelectMany(plan => plan.Modules.Select(module => new { module.ModuleCode, module.Roles }))
                .ToListAsync(cancellationToken))
                .ToDictionary(module => module.ModuleCode, module => module.Roles, StringComparer.Ordinal);

        var overrides = await catalog.TenantModuleOverrides.AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        return new TenantModuleSource(modules, planModules, overrides);
    }
}
