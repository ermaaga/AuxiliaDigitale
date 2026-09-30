using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Platform.Modules;
using Auxilia.Application.Tests.Configuration;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using NSubstitute;

namespace Auxilia.Application.Tests.Platform.Modules;

public sealed class ModuleCatalogManagerTests
{
    private readonly ICatalogStore catalog = Substitute.For<ICatalogStore>();
    private readonly FakeReferenceDataCache cache = new();
    private readonly List<PlatformModule> stored = [];
    private readonly Plan plan = Plan.Create(Plan.StandardId, Plan.StandardCode, "platform.plans.standard", isDefault: true).Value;

    public ModuleCatalogManagerTests()
    {
        catalog.ListModulesAsync(Arg.Any<CancellationToken>()).Returns(_ => stored.ToList());
        catalog.When(store => store.Add(Arg.Any<PlatformModule>())).Do(call => stored.Add(call.Arg<PlatformModule>()));
        catalog.DefaultPlanIdAsync(Arg.Any<CancellationToken>()).Returns(Plan.StandardId);
        catalog.FindPlanAsync(Plan.StandardId, Arg.Any<CancellationToken>()).Returns(plan);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sync_AddsNewModulesToTheDefaultPlanForEveryRole()
    {
        var result = await Manager(new TestModule("identity", ModuleKind.Core, 12000), new TestModule("cases", rangeStart: 14000)).SyncAsync(Ct);

        result.Value.Added.ShouldBe(["cases", "identity"]);
        result.Value.Unavailable.ShouldBeEmpty();
        stored.Single(module => module.Id == "identity").Kind.ShouldBe(ModuleKind.Core);
        stored.Single(module => module.Id == "cases").NameKey.ShouldBe("modules.cases.name");
        plan.RolesFor("cases").ShouldBe([TenantRole.Administrator, TenantRole.Employee, TenantRole.Client]);
        cache.Invalidated.ShouldBe(["platform:platform"]);
    }

    [Fact]
    public async Task Sync_KeepsSystemChoicesForKnownModulesAndMarksRemovedOnesUnavailable()
    {
        stored.Add(new PlatformModule("cases", ModuleKind.Core, "old", 1));
        stored.Add(new PlatformModule("training", ModuleKind.Optional, "modules.training.name", 18500));
        plan.SetModule("cases", [TenantRole.Administrator]);

        var result = await Manager(new TestModule("cases", rangeStart: 14000)).SyncAsync(Ct);

        result.Value.Added.ShouldBeEmpty();
        result.Value.Unavailable.ShouldBe(["training"]);
        var cases = stored.Single(module => module.Id == "cases");
        (cases.Kind, cases.NameKey, cases.EventCodeRangeStart, cases.IsAvailable).ShouldBe((ModuleKind.Optional, "modules.cases.name", 14000, true));
        stored.Single(module => module.Id == "training").IsAvailable.ShouldBeFalse();
        plan.RolesFor("cases").ShouldBe([TenantRole.Administrator]);
        await catalog.DidNotReceive().FindPlanAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        (await Manager(new TestModule("cases", rangeStart: 14000)).SyncAsync(Ct)).Value.Unavailable.ShouldBeEmpty();
    }

    private ModuleCatalogManager Manager(params TestModule[] modules) =>
        new(ManagerHarness.Runner(), new ModuleRegistry(modules), catalog, cache);
}
