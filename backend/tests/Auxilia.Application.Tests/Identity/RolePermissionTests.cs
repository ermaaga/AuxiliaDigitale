using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Identity;
using Auxilia.Application.Platform.Modules;
using Auxilia.Application.Tests.Configuration;
using Auxilia.Application.Tests.Platform;
using Auxilia.Application.Tests.Platform.Modules;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace Auxilia.Application.Tests.Identity;

/// <summary>In-memory <c>identity.role_permissions</c> shared by the units of work and the reader.</summary>
internal sealed class InMemoryRolePermissionData : IRolePermissionDataFactory, IRolePermissionData, IRolePermissionReader
{
    public List<RoleGrant> Grants { get; } = [];

    public int Saves { get; private set; }

    public Task<IRolePermissionData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<IRolePermissionData>(this);

    public Task<IReadOnlyList<RoleGrant>> GrantsAsync(TenantRole role, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RoleGrant>>(Grants.Where(grant => grant.Role == role).ToArray());

    public void Add(RoleGrant grant) => Grants.Add(grant);

    public void Remove(RoleGrant grant) => Grants.Remove(grant);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }

    public Task<RolePermissionGrants> ReadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new RolePermissionGrants(Grants.GroupBy(grant => grant.Role).ToDictionary(group => group.Key, group => group.Select(grant => grant.PermissionCode).ToArray())));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class RolePermissionTests
{
    private const string View = "people.clients.view";
    private const string Manage = "people.clients.manage";
    private const string Report = "reports.dashboard.view";

    private static readonly TenantInfo Acme = new(Guid.CreateVersion7(), "acme", TenantStatus.Active, "it", "Europe/Rome");

    private InMemoryRolePermissionData data { get; } = new();
    private readonly FakeReferenceDataCache cache = new();
    private readonly ITenantContext tenantContext = Substitute.For<ITenantContext>();
    private readonly RolePermissionManager manager;
    private readonly RolePermissionQueryService query;

    public RolePermissionTests()
    {
        tenantContext.Current.Returns(Acme);
        tenantContext.Tenant.Returns(Acme);
        tenantContext.IsResolved.Returns(true);
        var modules = new ModuleRegistry(
        [
            new TestModule("people", permissions: [new(View, [TenantRole.Administrator, TenantRole.Employee]), new(Manage, [TenantRole.Administrator])]),
            new TestModule("reports", rangeStart: 91000, permissions: [new(Report, [TenantRole.Client, TenantRole.Administrator, TenantRole.Client])]),
        ]);
        manager = new RolePermissionManager(ManagerHarness.Runner(), data, modules, tenantContext, cache, NullLogger<RolePermissionManager>.Instance);
        query = new RolePermissionQueryService(modules, new RolePermissionsCache(cache, tenantContext, data));
        data.Grants.AddRange([new(TenantRole.Administrator, View), new(TenantRole.Administrator, Manage), new(TenantRole.Employee, View)]);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_EveryDeclaredPermissionInModuleOrderWithItsRoles()
    {
        var list = await query.ListAsync(Ct);

        list.Select(permission => permission.Code).ShouldBe([View, Manage, Report]);
        list[0].Module.ShouldBe("people");
        list[0].Roles.ShouldBe(["Administrator", "Employee"]);
        list[2].Roles.ShouldBeEmpty();
        list[2].DefaultRoles.ShouldBe(["Administrator", "Client"]);
    }

    [Fact]
    public async Task Set_GrantsAndRevokesThenInvalidatesTheGrants()
    {
        (await query.ListAsync(Ct))[1].Roles.ShouldBe(["Administrator"]);

        (await manager.SetAsync("Employee", [Manage, Report, Manage], Ct)).IsSuccess.ShouldBeTrue();

        data.Grants.Where(grant => grant.Role == TenantRole.Employee).Select(grant => grant.PermissionCode).ShouldBe([Manage, Report], ignoreOrder: true);
        cache.Invalidated.ShouldBe(["t:acme:identity"]);
        var list = await query.ListAsync(Ct);
        list[0].Roles.ShouldBe(["Administrator"]);
        list[1].Roles.ShouldBe(["Administrator", "Employee"]);
    }

    [Fact]
    public async Task Set_WithoutChanges_SavesNothing()
    {
        (await manager.SetAsync("Employee", [View], Ct)).IsSuccess.ShouldBeTrue();

        data.Saves.ShouldBe(0);
        cache.Invalidated.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("administrator", "role")]
    [InlineData("System", "role")]
    [InlineData("Employee", "permissions")]
    public async Task Set_UnknownRoleOrPermission_IsRefused(string role, string field)
    {
        var result = await manager.SetAsync(role, [View, "people.clients.fly"], Ct);

        result.Error!.Code.ShouldBe(EventCodes.Identity.RolePermissionsInvalid);
        result.Error.ValidationErrors!.Keys.ShouldBe([field]);
        data.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Set_NullPermissions_IsRefused() =>
        (await manager.SetAsync("Client", null, Ct)).Error!.ValidationErrors!.Keys.ShouldBe(["permissions"]);

    [Fact]
    public async Task Reset_RestoresTheDefaultRolesOfTheModules()
    {
        await manager.SetAsync("Administrator", [], Ct);
        data.Grants.ShouldAllBe(grant => grant.Role != TenantRole.Administrator);

        (await manager.ResetAsync("Administrator", Ct)).IsSuccess.ShouldBeTrue();
        (await manager.ResetAsync("Client", Ct)).IsSuccess.ShouldBeTrue();

        data.Grants.Where(grant => grant.Role == TenantRole.Administrator).Select(grant => grant.PermissionCode).ShouldBe([Manage, Report, View], ignoreOrder: true);
        data.Grants.Where(grant => grant.Role == TenantRole.Client).Select(grant => grant.PermissionCode).ShouldBe([Report]);
        (await manager.ResetAsync("Nobody", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.RolePermissionsInvalid);
    }
}
