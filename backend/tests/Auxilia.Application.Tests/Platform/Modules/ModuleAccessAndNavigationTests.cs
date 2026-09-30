using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Platform.Modules;
using Auxilia.Application.Tests.Configuration;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using NSubstitute;

using static Auxilia.Application.Tests.Platform.Modules.Roles;

namespace Auxilia.Application.Tests.Platform.Modules;

public sealed class ModuleAccessAndNavigationTests
{
    private static readonly TenantInfo Acme = new(Guid.CreateVersion7(), "acme", TenantStatus.Active, "it", "Europe/Rome");

    private readonly ITenantContext tenantContext = Substitute.For<ITenantContext>();
    private readonly ICurrentUser user = Substitute.For<ICurrentUser>();
    private readonly IModuleCatalogReader catalog = Substitute.For<IModuleCatalogReader>();
    private readonly FakeReferenceDataCache cache = new();

    public ModuleAccessAndNavigationTests()
    {
        tenantContext.Current.Returns(Acme);
        tenantContext.Tenant.Returns(Acme);
        catalog.GetSourceAsync(Acme.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new TenantModuleSource(
            [new CatalogModule("identity", ModuleKind.Core), new CatalogModule("cases", ModuleKind.Optional), new CatalogModule("marketing", ModuleKind.Optional)],
            new Dictionary<string, TenantRole[]> { ["cases"] = [Admin, Employee, Client], ["marketing"] = [Admin] },
            []));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task IsVisible_UsesTheRolesOfTheUserAndCachesPerTenant()
    {
        var access = Access(ActorType.User, Employee);

        (await access.IsVisibleAsync("cases", Ct)).ShouldBeTrue();
        (await access.IsVisibleAsync("marketing", Ct)).ShouldBeFalse();
        (await access.IsVisibleAsync("identity", Ct)).ShouldBeTrue();

        cache.Loads.ShouldBe(1);
        var entry = cache.Requests[0];
        entry.Key.ShouldBe("t:acme:platform:modules:current");
        entry.Tags.ShouldBe(["t:acme:platform", "platform:platform"]);
    }

    [Theory]
    [InlineData(ActorType.Anonymous)]
    [InlineData(ActorType.Platform)]
    [InlineData(ActorType.System)]
    public async Task IsVisible_NonTenantUsers_SeeNothing(ActorType actor)
    {
        (await Access(actor, Admin).IsVisibleAsync("identity", Ct)).ShouldBeFalse();
        (await Access(ActorType.User).IsVisibleAsync("identity", Ct)).ShouldBeFalse();
        cache.Loads.ShouldBe(0);
    }

    [Fact]
    public async Task WithoutTenant_NoModules()
    {
        tenantContext.Current.Returns((TenantInfo?)null);

        (await Access(ActorType.User, Admin).GetAsync(Ct)).Modules.ShouldBeEmpty();
    }

    [Fact]
    public async Task Navigation_IsTheOrderedUnionForTheRolesOfTheUser()
    {
        var registry = new ModuleRegistry(
        [
            new TestModule("identity", ModuleKind.Core, 12000, [new("sessions", "/sessions", "monitor", 90, [Admin])]),
            new TestModule("cases", rangeStart: 14000, navigation:
            [
                new("cases", "/cases", "briefcase", 30, [Admin, Employee, Client]),
                new("services", "/services", "layers", 40, [Admin]),
            ]),
            new TestModule("marketing", rangeStart: 19000, navigation: [new("marketing", "/marketing", "megaphone", 80, [Admin, Employee])]),
            new TestModule("reporting", rangeStart: 27000, navigation: [new("dashboard", "/dashboard", "layout", 0, [Admin, Employee, Client])]),
        ]);

        var employeeAndAdmin = await new NavigationQueryService(registry, Access(ActorType.User, Employee, Admin), user, AllGranted()).GetAsync(Ct);
        var client = await new NavigationQueryService(registry, Access(ActorType.User, Client), user, AllGranted()).GetAsync(Ct);
        var employee = await new NavigationQueryService(registry, Access(ActorType.User, Employee), user, AllGranted()).GetAsync(Ct);

        // reporting is not in the tenant's catalog; marketing is in the plan for Administrator only.
        employeeAndAdmin.Select(item => item.Key).ShouldBe(["cases", "services", "marketing", "sessions"]);
        client.Select(item => item.Key).ShouldBe(["cases"]);
        employee.Select(item => item.Key).ShouldBe(["cases"]);
        var cases = client[0];
        (cases.Module, cases.LabelKey, cases.Route, cases.Icon, cases.Order).ShouldBe(("cases", "nav.cases", "/cases", "briefcase", 30));
        (await new NavigationQueryService(registry, Access(ActorType.Anonymous), user, AllGranted()).GetAsync(Ct)).ShouldBeEmpty();
    }

    private static IPermissionAccess AllGranted()
    {
        var permissions = Substitute.For<IPermissionAccess>();
        permissions.GetGrantedAsync(Arg.Any<CancellationToken>()).Returns(new HashSet<string>(StringComparer.Ordinal));
        return permissions;
    }

    private ModuleAccess Access(ActorType actor, params TenantRole[] roles)
    {
        user.ActorType.Returns(actor);
        user.Roles.Returns(roles);
        return new ModuleAccess(new TenantModulesCache(cache, tenantContext, catalog, TimeProvider.System), user);
    }
}
