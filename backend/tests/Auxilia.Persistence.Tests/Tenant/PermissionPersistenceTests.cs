using Auxilia.Application;
using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Cases;
using Auxilia.Application.Identity;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Platform;
using Auxilia.Infrastructure;
using Auxilia.Persistence.Tenant;
using Auxilia.Persistence.Tenant.Identity;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using NSubstitute;

namespace Auxilia.Persistence.Tests.Tenant;

/// <summary>F22 on PostgreSQL: permissions follow the module descriptors, default grants only for new permissions.</summary>
[Collection(TenantDatabaseGroup.Name)]
public sealed class PermissionPersistenceTests(TenantDatabaseFixture database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Synchronizer_GrantsDefaultsOnce_KeepsTheSystemsChoices_AndDropsUndeclaredPermissions()
    {
        var connectionString = await database.CreateDatabaseAsync("permissions_" + Guid.NewGuid().ToString("N")[..8]);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var services = Services(connectionString);
        var synchronizer = services.GetRequiredService<PermissionSynchronizer>();
        var registry = services.GetRequiredService<IModuleRegistry>();

        await using (var db = Context(dataSource))
        {
            await db.Database.MigrateAsync(Ct);
            await synchronizer.ApplyAsync(db, Ct);
        }

        await using (var db = Context(dataSource))
        {
            (await db.Set<PermissionEntry>().CountAsync(Ct)).ShouldBe(registry.PermissionModules.Count);
            var cases = await db.Set<RoleGrant>().Where(grant => grant.PermissionCode == CasesPermissions.ManageCases).Select(grant => grant.Role).ToListAsync(Ct);
            cases.ShouldBe([TenantRole.Administrator, TenantRole.Employee], ignoreOrder: true);

            // The System takes cases.manage away from Employee; a permission of a removed module is left behind.
            db.Set<RoleGrant>().Remove(await db.Set<RoleGrant>().SingleAsync(grant => grant.Role == TenantRole.Employee && grant.PermissionCode == CasesPermissions.ManageCases, Ct));
            db.Set<PermissionEntry>().Add(new PermissionEntry("training.plans.view", "training"));
            db.Set<RoleGrant>().Add(new RoleGrant(TenantRole.Client, "training.plans.view"));
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = Context(dataSource))
        {
            await synchronizer.ApplyAsync(db, Ct);
        }

        await using (var db = Context(dataSource))
        {
            (await db.Set<RoleGrant>().AnyAsync(grant => grant.Role == TenantRole.Employee && grant.PermissionCode == CasesPermissions.ManageCases, Ct)).ShouldBeFalse();
            (await db.Set<PermissionEntry>().AnyAsync(permission => permission.Code == "training.plans.view", Ct)).ShouldBeFalse();
            (await db.Set<RoleGrant>().AnyAsync(grant => grant.PermissionCode == "training.plans.view", Ct)).ShouldBeFalse();
        }

        await using var scope = services.CreateAsyncScope();
        var grants = await scope.ServiceProvider.GetRequiredService<IRolePermissionReader>().ReadAsync(Ct);
        grants.Of(TenantRole.Administrator).ShouldContain(IdentityPermissions.ViewSessions);
        grants.Of(TenantRole.Employee).ShouldNotContain(CasesPermissions.ManageCases);
        grants.Of(TenantRole.Client).ShouldBe(grants.Of(TenantRole.Client).Order(StringComparer.Ordinal));
        grants.Of(TenantRole.Client).ShouldNotContain(IdentityPermissions.ViewSessions);
    }

    [Fact]
    public async Task EffectivePermissions_FromTheDatabase_ForAnEmployee()
    {
        var connectionString = await database.CreateDatabaseAsync("permissions_" + Guid.NewGuid().ToString("N")[..8]);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var services = Services(connectionString, TenantRole.Employee);
        await using (var db = Context(dataSource))
        {
            await db.Database.MigrateAsync(Ct);
            await services.GetRequiredService<PermissionSynchronizer>().ApplyAsync(db, Ct);
        }

        await using var scope = services.CreateAsyncScope();
        var granted = await scope.ServiceProvider.GetRequiredService<IPermissionAccess>().GetGrantedAsync(Ct);

        granted.ShouldContain(CasesPermissions.ManageCases);
        granted.ShouldNotContain(CasesPermissions.ManageServices);
        granted.ShouldNotContain(IdentityPermissions.ViewSessions);
    }

    private static TenantDbContext Context(NpgsqlDataSource dataSource) => new(TenantDbContextOptions.Create(dataSource));

    private static ServiceProvider Services(string connectionString, params TenantRole[] roles)
    {
        var tenant = new TenantInfo(Guid.CreateVersion7(), "tenant-perm", TenantStatus.Active, "it", "Europe/Rome");
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.Current.Returns(tenant);
        tenantContext.Tenant.Returns(tenant);
        var directory = Substitute.For<ITenantDirectory>();
        directory.GetProtectedConnectionStringAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns("protected");
        var protector = Substitute.For<ITenantConnectionProtector>();
        protector.Unprotect("protected").Returns(connectionString);
        var modules = Substitute.For<IModuleCatalogReader>();
        var all = Application.DependencyInjection.Modules.ToArray();
        modules.GetSourceAsync(tenant.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new TenantModuleSource(
            all.Select(module => new CatalogModule(module.Code, module.Kind)).ToArray(),
            all.ToDictionary(module => module.Code, _ => Enum.GetValues<TenantRole>()),
            []));
        var user = Substitute.For<ICurrentUser>();
        user.ActorType.Returns(roles.Length == 0 ? ActorType.System : ActorType.User);
        user.Roles.Returns(roles);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure();
        services.AddScoped(_ => tenantContext);
        services.AddScoped(_ => directory);
        services.AddSingleton(protector);
        services.AddScoped(_ => user);
        services.AddSingleton(modules);
        services.AddScoped(_ => Substitute.For<IPlatformSettingStore>());
        services.AddSingleton(Substitute.For<ISettingSecretProtector>());
        services.AddApplication();
        services.AddTenantPersistence();
        return services.BuildServiceProvider(validateScopes: true);
    }
}
