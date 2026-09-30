using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Identity;
using Auxilia.Application.Platform.Modules;
using Auxilia.Application.Tests.Configuration;
using Auxilia.Application.Tests.Execution;
using Auxilia.Application.Tests.Platform.Modules;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using static Auxilia.Application.Tests.Platform.Modules.Roles;

namespace Auxilia.Application.Tests.Identity;

public sealed class PermissionTests : IAsyncDisposable
{
    private static readonly TenantInfo Acme = new(Guid.CreateVersion7(), "acme", TenantStatus.Active, "it", "Europe/Rome");

    private readonly ITenantContext tenantContext = Substitute.For<ITenantContext>();
    private readonly ICurrentUser user = Substitute.For<ICurrentUser>();
    private readonly IModuleCatalogReader catalog = Substitute.For<IModuleCatalogReader>();
    private readonly IRolePermissionReader reader = Substitute.For<IRolePermissionReader>();
    private readonly FakeReferenceDataCache cache = new();
    private readonly InMemoryIdentityData identity = new();
    private readonly ModuleRegistry registry = new(
    [
        new TestModule("identity", ModuleKind.Core, 12000,
            [new("sessions", "/sessions", "monitor", 90, [Admin], "identity.sessions.view")],
            [new("identity.sessions.view", [Admin])]),
        new TestModule("cases", rangeStart: 14000, navigation:
            [
                new("cases", "/cases", "briefcase", 30, [Admin, Employee, Client], "cases.cases.view"),
                new("services", "/services", "layers", 40, [Admin], "cases.services.manage"),
            ],
            permissions:
            [
                new("cases.cases.view", [Admin, Employee, Client]),
                new("cases.cases.manage", [Admin, Employee]),
                new("cases.services.manage", [Admin]),
            ]),
        new TestModule("marketing", rangeStart: 19000,
            navigation: [new("marketing", "/marketing", "megaphone", 80, [Admin, Employee], "marketing.campaigns.view")],
            permissions: [new("marketing.campaigns.view", [Admin, Employee])]),
    ]);

    public PermissionTests()
    {
        tenantContext.Current.Returns(Acme);
        tenantContext.Tenant.Returns(Acme);
        catalog.GetSourceAsync(Acme.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new TenantModuleSource(
            [new CatalogModule("identity", ModuleKind.Core), new CatalogModule("cases", ModuleKind.Optional), new CatalogModule("marketing", ModuleKind.Optional)],
            new Dictionary<string, TenantRole[]> { ["cases"] = [Admin, Employee, Client], ["marketing"] = [Admin] },
            []));

        // Employee was also granted marketing (module not in the plan for Employee) and a permission no module declares.
        reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(new RolePermissionGrants(new Dictionary<TenantRole, string[]>
        {
            [Admin] = ["cases.cases.manage", "cases.cases.view", "cases.services.manage", "identity.sessions.view", "marketing.campaigns.view"],
            [Employee] = ["cases.cases.manage", "cases.cases.view", "marketing.campaigns.view", "legacy.gone.view"],
            [Client] = ["cases.cases.view"],
        }));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => identity.DisposeAsync();

    [Fact]
    public void Registry_MapsEveryPermissionToItsModule() =>
        registry.PermissionModules.ShouldBe(new Dictionary<string, string>
        {
            ["identity.sessions.view"] = "identity",
            ["cases.cases.view"] = "cases",
            ["cases.cases.manage"] = "cases",
            ["cases.services.manage"] = "cases",
            ["marketing.campaigns.view"] = "marketing",
        }, ignoreOrder: true);

    [Theory]
    [InlineData("cases.view")]
    [InlineData("other.cases.view")]
    [InlineData("cases.Cases.view")]
    [InlineData("cases.cases.view.all")]
    [InlineData("cases..view")]
    public void Registry_RejectsMalformedPermissionCodes(string code) =>
        Should.Throw<InvalidOperationException>(() => new ModuleRegistry([new TestModule("cases", permissions: [new(code, [Admin])])]));

    [Fact]
    public void Registry_RejectsDuplicatesAndNavigationWithoutADeclaredPermission()
    {
        Should.Throw<InvalidOperationException>(() => new ModuleRegistry(
            [new TestModule("cases", permissions: [new("cases.cases.view", [Admin]), new("cases.cases.view", [Client])])]));
        Should.Throw<InvalidOperationException>(() => new ModuleRegistry(
        [
            new TestModule("cases", rangeStart: 14000, permissions: [new("cases.cases.view", [Admin])]),
            new TestModule("marketing", rangeStart: 19000, navigation: [new("marketing", "/marketing", "m", 1, [Admin], "cases.cases.view")]),
        ])).Message.ShouldContain("does not declare");
    }

    [Fact]
    public async Task Granted_IsTheUnionOfTheRoles_LimitedToModulesVisibleToEachRole()
    {
        (await Permissions(Employee).GetGrantedAsync(Ct)).ShouldBe(["cases.cases.manage", "cases.cases.view"], ignoreOrder: true);
        (await Permissions(Client).GetGrantedAsync(Ct)).ShouldBe(["cases.cases.view"]);
        (await Permissions(Client, Admin).GetGrantedAsync(Ct)).ShouldBe(
            ["cases.cases.manage", "cases.cases.view", "cases.services.manage", "identity.sessions.view", "marketing.campaigns.view"], ignoreOrder: true);

        var entry = cache.Requests.First(request => request.Key.Contains("role-permissions", StringComparison.Ordinal));
        entry.Key.ShouldBe("t:acme:identity:role-permissions:current");
        entry.Tags.ShouldBe(["t:acme:identity", "platform:identity"]);
    }

    [Theory]
    [InlineData(ActorType.Anonymous)]
    [InlineData(ActorType.Platform)]
    [InlineData(ActorType.System)]
    public async Task Granted_IsEmptyForNonTenantUsers(ActorType actor)
    {
        var permissions = Permissions(Admin);
        user.ActorType.Returns(actor);

        (await permissions.HasAsync("cases.cases.view", Ct)).ShouldBeFalse();
        (await Permissions().HasAsync("cases.cases.view", Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Granted_IsComputedOncePerScope_AndWithoutTenantThereAreNoGrants()
    {
        var permissions = Permissions(Employee);
        await permissions.GetGrantedAsync(Ct);
        await permissions.GetGrantedAsync(Ct);
        await reader.Received(1).ReadAsync(Arg.Any<CancellationToken>());

        tenantContext.Current.Returns((TenantInfo?)null);
        (await Permissions(Admin).GetGrantedAsync(Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Guard_DeniesAndLogsAMissingPermission()
    {
        var log = new RecordingLogger<AccessGuard>();
        var guard = Guard(Permissions(Client), new ServiceCollection().BuildServiceProvider(), log);

        (await guard.EnsureAsync("cases.cases.view", Ct)).IsSuccess.ShouldBeTrue();
        (await guard.EnsureAsync("cases.cases.manage", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);

        var entry = log.Entries.ShouldHaveSingleItem();
        entry.EventId.Id.ShouldBe(EventCodes.Security.PermissionDenied);
        entry.Properties["Permission"].ShouldBe("cases.cases.manage");
        entry.Properties["Reason"].ShouldBe("MissingPermission");
    }

    [Fact]
    public async Task Guard_AppliesEveryResourcePolicyAfterThePermission()
    {
        var log = new RecordingLogger<AccessGuard>();
        var services = new ServiceCollection()
            .AddSingleton<IResourceAccessPolicy<OwnedThing>>(new OwnerPolicy(user))
            .AddSingleton<IResourceAccessPolicy<OwnedThing>>(new NotArchivedPolicy())
            .BuildServiceProvider();
        var guard = Guard(Permissions(Client), services, log);
        user.UserId.Returns(Guid.Parse("0199a0b2-0000-7000-8000-000000000001"));

        (await guard.EnsureAsync("cases.cases.view", new OwnedThing(user.UserId!.Value, false), Ct)).IsSuccess.ShouldBeTrue();
        (await guard.EnsureAsync("cases.cases.view", new OwnedThing(Guid.CreateVersion7(), false), Ct)).Error!.Code
            .ShouldBe(EventCodes.Identity.PermissionDenied);
        (await guard.EnsureAsync("cases.cases.view", new OwnedThing(user.UserId!.Value, true), Ct)).IsFailure.ShouldBeTrue();
        (await guard.EnsureAsync("cases.cases.manage", new OwnedThing(user.UserId!.Value, false), Ct)).IsFailure.ShouldBeTrue();

        log.Entries.Select(entry => entry.Properties["Reason"]).ShouldBe(["ResourcePolicy", "ResourcePolicy", "MissingPermission"]);
        await Should.ThrowAsync<InvalidOperationException>(() => guard.EnsureAsync("cases.cases.view", "no policy for strings", Ct));
    }

    [Fact]
    public async Task Navigation_HidesEntriesWithoutThePermission()
    {
        reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(new RolePermissionGrants(new Dictionary<TenantRole, string[]>
        {
            // The System removed services from Administrator; Client lost cases.
            [Admin] = ["cases.cases.view", "identity.sessions.view", "marketing.campaigns.view"],
            [Client] = [],
        }));

        var admin = await Navigation(Admin).GetAsync(Ct);
        var client = await Navigation(Client).GetAsync(Ct);

        admin.Select(item => item.Key).ShouldBe(["cases", "marketing", "sessions"]);
        client.ShouldBeEmpty();
    }

    [Fact]
    public async Task Me_ReturnsTheAccountRolesAndEffectivePermissions()
    {
        var account = User.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "anna.bianchi", "anna@example.test", "en", [Employee, Admin], isActive: true).Value;
        identity.Users.Add(account);
        var permissions = Permissions(Employee, Admin);
        user.UserId.Returns(account.Id);

        var me = (await new CurrentUserQueryService(user, identity, permissions, tenantContext).GetAsync(Ct)).Value;

        (me.Id, me.UserName, me.Email, me.Language, me.Tenant).ShouldBe((account.Id, "anna.bianchi", "anna@example.test", "en", "acme"));
        me.Roles.ShouldBe(["Administrator", "Employee"]);
        me.Permissions.ShouldBe(["cases.cases.manage", "cases.cases.view", "cases.services.manage", "identity.sessions.view", "marketing.campaigns.view"]);
    }

    [Fact]
    public async Task Me_UnknownInactiveOrNonTenantCaller_IsNotFound()
    {
        var inactive = User.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "gone", null, "it", [Client], isActive: false).Value;
        identity.Users.Add(inactive);
        var permissions = Permissions(Client);
        var query = new CurrentUserQueryService(user, identity, permissions, tenantContext);

        user.UserId.Returns(inactive.Id);
        (await query.GetAsync(Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNotFound);
        user.UserId.Returns(Guid.CreateVersion7());
        (await query.GetAsync(Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNotFound);
        user.ActorType.Returns(ActorType.Platform);
        (await query.GetAsync(Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNotFound);
    }

    private PermissionAccess Permissions(params TenantRole[] roles)
    {
        user.ActorType.Returns(ActorType.User);
        user.Roles.Returns(roles);
        var modules = new ModuleAccess(new TenantModulesCache(cache, tenantContext, catalog, TimeProvider.System), user);
        return new PermissionAccess(new RolePermissionsCache(cache, tenantContext, reader), modules, registry, user);
    }

    private NavigationQueryService Navigation(params TenantRole[] roles)
    {
        var permissions = Permissions(roles);
        return new NavigationQueryService(registry, new ModuleAccess(new TenantModulesCache(cache, tenantContext, catalog, TimeProvider.System), user), user, permissions);
    }

    private AccessGuard Guard(IPermissionAccess permissions, IServiceProvider services, RecordingLogger<AccessGuard> log) =>
        new(permissions, user, services, log);

    private sealed record OwnedThing(Guid OwnerId, bool IsArchived);

    private sealed class OwnerPolicy(ICurrentUser currentUser) : IResourceAccessPolicy<OwnedThing>
    {
        public Task<bool> CanAccessAsync(OwnedThing resource, string permission, CancellationToken cancellationToken) =>
            Task.FromResult(resource.OwnerId == currentUser.UserId);
    }

    private sealed class NotArchivedPolicy : IResourceAccessPolicy<OwnedThing>
    {
        public Task<bool> CanAccessAsync(OwnedThing resource, string permission, CancellationToken cancellationToken) =>
            Task.FromResult(!resource.IsArchived);
    }
}
