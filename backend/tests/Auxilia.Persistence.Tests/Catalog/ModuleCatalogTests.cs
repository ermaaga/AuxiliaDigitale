using Auxilia.Application;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Platform.Modules;
using Auxilia.Domain.Platform;
using Auxilia.Infrastructure;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Persistence.Tests.Catalog;

/// <summary><c>catalog.modules</c> generated from the descriptors, and the catalog data behind the effective modules.</summary>
[Collection(CatalogDatabaseGroup.Name)]
public sealed class ModuleCatalogTests(CatalogDatabaseFixture database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sync_CreatesEveryModuleInTheStandardPlanAndIsIdempotent()
    {
        await using var services = Services();

        await SyncAsync(services);
        var second = await SyncAsync(services);

        second.Added.ShouldBeEmpty();
        await using var db = database.CreateContext();
        var modules = await db.Modules.Where(module => module.IsAvailable).Select(module => module.Id).ToListAsync(Ct);
        modules.ShouldBe(Auxilia.Application.DependencyInjection.Modules.Select(module => module.Code), ignoreOrder: true);
        var standard = await db.Plans.Include(plan => plan.Modules).SingleAsync(plan => plan.Id == Plan.StandardId, Ct);
        foreach (var module in Auxilia.Application.DependencyInjection.Modules)
        {
            standard.RolesFor(module.Code).ShouldBe([TenantRole.Administrator, TenantRole.Employee, TenantRole.Client]);
        }
    }

    [Fact]
    public async Task Reader_ReturnsThePlanValidNowAndTheOverridesOfTheTenant()
    {
        await using var services = Services();
        await SyncAsync(services);
        var tenantId = await AddTenantAsync();
        var now = DateTimeOffset.UtcNow;

        await using (var db = database.CreateContext())
        {
            var basic = Plan.Create(Guid.CreateVersion7(), "basic-" + Guid.NewGuid().ToString("N")[..6], "platform.plans.basic", isDefault: false).Value;
            basic.SetModule("cases", [TenantRole.Administrator]);
            db.Plans.Add(basic);
            db.TenantPlans.Add(new TenantPlan(Guid.CreateVersion7(), tenantId, Plan.StandardId, now.AddDays(-30), now.AddDays(-1)));
            db.TenantPlans.Add(new TenantPlan(Guid.CreateVersion7(), tenantId, basic.Id, now.AddDays(-1)));
            db.TenantModuleOverrides.Add(new TenantModuleOverride(tenantId, "marketing", isEnabled: true, [TenantRole.Employee]));
            await db.SaveChangesAsync(Ct);
        }

        await using var scope = services.CreateAsyncScope();
        var source = await scope.ServiceProvider.GetRequiredService<IModuleCatalogReader>().GetSourceAsync(tenantId, now, Ct);

        source.Modules.ShouldContain(new CatalogModule("identity", ModuleKind.Core));
        source.PlanModules.Keys.ShouldBe(["cases"]);
        source.PlanModules["cases"].ShouldBe([TenantRole.Administrator]);
        source.Overrides.ShouldHaveSingleItem().ModuleCode.ShouldBe("marketing");

        var withoutPlan = await scope.ServiceProvider.GetRequiredService<IModuleCatalogReader>().GetSourceAsync(tenantId, now.AddDays(-60), Ct);
        withoutPlan.PlanModules.ShouldBeEmpty();
    }

    private ServiceProvider Services() => database.CreateServices(services =>
    {
        services.AddApplication();
        services.AddInfrastructure();
    });

    private static async Task<ModuleSyncReport> SyncAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IModuleCatalogManager>().SyncAsync(Ct);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private async Task<Guid> AddTenantAsync()
    {
        await using var db = database.CreateContext();
        var tenant = Auxilia.Domain.Platform.Tenant.Create(Guid.CreateVersion7(), "modules-" + Guid.NewGuid().ToString("N")[..8], "Modules", "it", "Europe/Rome").Value;
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(Ct);
        return tenant.Id;
    }
}
