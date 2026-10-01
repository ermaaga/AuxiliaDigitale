using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Platform;
using Auxilia.Application.Tests.Identity;
using Auxilia.Diagnostics;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using NSubstitute;

namespace Auxilia.Application.Tests.Platform;

public sealed class PlatformConsoleQueryServiceTests
{
    private readonly ICatalogStore catalog = Substitute.For<ICatalogStore>();
    private readonly IModuleCatalogReader modules = Substitute.For<IModuleCatalogReader>();
    private readonly ManualTimeProvider clock = new();
    private readonly Tenant tenant = Tenant.Create(Guid.CreateVersion7(), "acme", "Acme", "it", "Europe/Rome").Value;
    private readonly PlatformConsoleQueryService console;

    public PlatformConsoleQueryServiceTests()
    {
        catalog.FindTenantAsync("acme", Arg.Any<CancellationToken>()).Returns(tenant);
        console = new PlatformConsoleQueryService(Substitute.For<ICurrentUser>(), Substitute.For<IPlatformIdentityStore>(), catalog, modules, clock);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tenant_HasItsPlanAndItsLatestRuns()
    {
        var standard = Plan.Create(Plan.StandardId, Plan.StandardCode, "plans.standard", isDefault: true).Value;
        var since = clock.GetUtcNow().AddDays(-3);
        catalog.CurrentTenantPlanAsync(tenant.Id, clock.GetUtcNow(), Arg.Any<CancellationToken>())
            .Returns(new TenantPlan(Guid.CreateVersion7(), tenant.Id, Plan.StandardId, since));
        catalog.FindPlanAsync(Plan.StandardId, Arg.Any<CancellationToken>()).Returns(standard);
        var run = new MigrationRun(Guid.CreateVersion7(), tenant.Id, MigrationRunKind.Provisioning, "acme", "platform:ops", since);
        run.Fail(since.AddMinutes(1), "AUX-28002", "NpgsqlException");
        catalog.RecentRunsAsync(tenant.Id, PlatformConsoleQueryService.RecentRuns, Arg.Any<CancellationToken>()).Returns([run]);

        var detail = (await console.GetTenantAsync("acme", Ct)).Value;

        (detail.Slug, detail.Status, detail.DefaultLanguage, detail.TimeZone).ShouldBe(("acme", "Provisioning", "it", "Europe/Rome"));
        detail.Plan.ShouldBe(new Contracts.Platform.TenantPlanResponse(Plan.StandardCode, "plans.standard", since));
        detail.Runs.ShouldHaveSingleItem().ShouldBe(new Contracts.Platform.MigrationRunResponse(
            "Provisioning", "Failed", since, since.AddMinutes(1), "AUX-28002", "NpgsqlException"));
        (await console.GetTenantAsync("unknown", Ct)).Error!.Code.ShouldBe(EventCodes.Tenancy.TenantNotFound);
    }

    [Fact]
    public async Task Modules_ShowPlanOverrideAndEffectiveRoles()
    {
        catalog.ListModulesAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new PlatformModule("identity", ModuleKind.Core, "modules.identity", 12000),
            new PlatformModule("cases", ModuleKind.Optional, "modules.cases", 14000),
            new PlatformModule("marketing", ModuleKind.Optional, "modules.marketing", 19000),
        ]);
        modules.GetSourceAsync(tenant.Id, clock.GetUtcNow(), Arg.Any<CancellationToken>()).Returns(new TenantModuleSource(
            [new CatalogModule("marketing", ModuleKind.Optional), new CatalogModule("identity", ModuleKind.Core), new CatalogModule("cases", ModuleKind.Optional)],
            new Dictionary<string, TenantRole[]>
            {
                ["cases"] = [TenantRole.Administrator, TenantRole.Employee, TenantRole.Client],
                ["marketing"] = [TenantRole.Administrator],
            },
            [new TenantModuleOverride(tenant.Id, "marketing", isEnabled: false, [])]));

        var result = (await console.GetTenantModulesAsync("acme", Ct)).Value;

        result.Select(module => module.Code).ShouldBe(["identity", "cases", "marketing"]);
        result[0].EffectiveRoles.ShouldBe(["Administrator", "Employee", "Client"]);
        result[1].Override.ShouldBeNull();
        result[1].EffectiveRoles.ShouldBe(["Administrator", "Employee", "Client"]);
        result[2].PlanRoles.ShouldBe(["Administrator"]);
        result[2].Override.ShouldBe(new Contracts.Platform.ModuleOverrideResponse(false, []));
        result[2].EffectiveRoles.ShouldBeEmpty();
        result[2].NameKey.ShouldBe("modules.marketing");
    }

    [Fact]
    public async Task Plans_AreTheActiveOnesWithModulesPerRole()
    {
        var standard = Plan.Create(Plan.StandardId, Plan.StandardCode, "plans.standard", isDefault: true).Value;
        standard.SetModule("cases", [TenantRole.Employee, TenantRole.Administrator]);
        catalog.ListPlansAsync(Arg.Any<CancellationToken>()).Returns([standard]);

        var plan = (await console.ListPlansAsync(Ct)).ShouldHaveSingleItem();

        (plan.Code, plan.IsDefault).ShouldBe((Plan.StandardCode, true));
        plan.Modules.ShouldHaveSingleItem().ShouldBe(new Contracts.Platform.PlanModuleResponse("cases", ["Administrator", "Employee"]), new ModuleComparer());
    }

    private sealed class ModuleComparer : IEqualityComparer<Contracts.Platform.PlanModuleResponse>
    {
        public bool Equals(Contracts.Platform.PlanModuleResponse? x, Contracts.Platform.PlanModuleResponse? y) =>
            x!.Code == y!.Code && x.Roles.SequenceEqual(y.Roles);

        public int GetHashCode(Contracts.Platform.PlanModuleResponse obj) => obj.Code.GetHashCode(StringComparison.Ordinal);
    }
}
