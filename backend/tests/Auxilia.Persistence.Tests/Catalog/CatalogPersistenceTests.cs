using Auxilia.Domain.Platform;
using Auxilia.Persistence.Catalog;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Persistence.Tests.Catalog;

[Collection(CatalogDatabaseGroup.Name)]
public sealed class CatalogPersistenceTests(CatalogDatabaseFixture database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SaveTenant_FillsAuditColumnsAndRoundTrips()
    {
        await using var services = database.CreateServices();
        var tenant = NewTenant();

        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(Ct);
        }

        await using var read = database.CreateContext();
        var stored = await read.Tenants.SingleAsync(item => item.Id == tenant.Id, Ct);
        stored.Slug.ShouldBe(tenant.Slug);
        stored.Status.ShouldBe(TenantStatus.Provisioning);
        read.Entry(stored).Property<string>("CreatedBy").CurrentValue.ShouldBe($"platform:{CatalogDatabaseFixture.ActorId}");
        read.Entry(stored).Property<DateTimeOffset>("CreatedAt").CurrentValue.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
        read.Entry(stored).Property<uint>("Version").CurrentValue.ShouldBeGreaterThan(0u);
    }

    [Fact]
    public async Task SaveTenant_SlugIsUniqueIgnoringCase()
    {
        await using var db = database.CreateContext();
        var slug = "dup-" + Guid.NewGuid().ToString("N")[..8];
        db.Tenants.Add(NewTenant(slug));
        await db.SaveChangesAsync(Ct);

        db.Tenants.Add(NewTenant(slug));

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task UpdateTenant_StaleVersion_ThrowsConcurrencyException()
    {
        var tenant = NewTenant();
        await using (var db = database.CreateContext())
        {
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(Ct);
        }

        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var a = await first.Tenants.SingleAsync(item => item.Id == tenant.Id, Ct);
        var b = await second.Tenants.SingleAsync(item => item.Id == tenant.Id, Ct);

        a.Activate();
        await first.SaveChangesAsync(Ct);
        b.Rename("Other name");

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task SavePlanModulesAndOverrides_RolesRoundTripAsTextArrays()
    {
        var code = "mod-" + Guid.NewGuid().ToString("N")[..8];
        var tenant = NewTenant();
        var plan = Plan.Create(Guid.CreateVersion7(), "plan-" + Guid.NewGuid().ToString("N")[..8], "platform.plans.test", isDefault: false).Value;
        plan.SetModule(code, [TenantRole.Employee, TenantRole.Administrator]);

        await using (var db = database.CreateContext())
        {
            db.Modules.Add(new PlatformModule(code, ModuleKind.Optional, "modules.test", 19000));
            db.Tenants.Add(tenant);
            db.Plans.Add(plan);
            db.TenantPlans.Add(new TenantPlan(Guid.CreateVersion7(), tenant.Id, plan.Id, DateTimeOffset.UtcNow));
            db.TenantModuleOverrides.Add(new TenantModuleOverride(tenant.Id, code, isEnabled: true, [TenantRole.Client]));
            await db.SaveChangesAsync(Ct);
        }

        await using var read = database.CreateContext();
        var storedPlan = await read.Plans.SingleAsync(item => item.Id == plan.Id, Ct);
        storedPlan.RolesFor(code).ShouldBe([TenantRole.Administrator, TenantRole.Employee]);
        var storedOverride = await read.TenantModuleOverrides.SingleAsync(item => item.TenantId == tenant.Id, Ct);
        storedOverride.Roles.ShouldBe([TenantRole.Client]);
    }

    [Fact]
    public async Task SaveTenantPlan_EndBeforeStart_IsRejectedByTheDatabase()
    {
        var tenant = NewTenant();
        await using var db = database.CreateContext();
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(Ct);

        await db.Database.ExecuteSqlAsync(
            $"insert into catalog.tenant_plans (id, tenant_id, plan_id, valid_from, valid_to, created_at, created_by) values ({Guid.CreateVersion7()}, {tenant.Id}, {Plan.StandardId}, now(), now() - interval '1 day', now(), 'test')",
            Ct).ShouldThrowAsync<Npgsql.PostgresException>();
    }

    [Fact]
    public async Task SavePlatformUserAndClientApplication_RoundTrip()
    {
        var user = new PlatformUser(Guid.CreateVersion7(), $"ops-{Guid.NewGuid():N}@auxilia.test", "Ops");
        var client = ClientApplication.Create(Guid.CreateVersion7(), "web-" + Guid.NewGuid().ToString("N")[..8], "Web", ClientApplicationType.WebBff).Value;
        client.SetAllowedOrigins(["https://app.auxilia.test/", "https://app.auxilia.test"]);

        await using (var db = database.CreateContext())
        {
            db.PlatformUsers.Add(user);
            db.ClientApplications.Add(client);
            db.PlatformSettings.Add(new PlatformSetting("test." + Guid.NewGuid().ToString("N"), """{"enabled":true}"""));
            await db.SaveChangesAsync(Ct);
        }

        await using var read = database.CreateContext();
        (await read.PlatformUsers.SingleAsync(item => item.Id == user.Id, Ct)).Roles.Single().Role.ShouldBe(PlatformUser.SystemRole);
        (await read.ClientApplications.SingleAsync(item => item.Id == client.Id, Ct)).AllowedOrigins.ShouldBe(["https://app.auxilia.test"]);
    }

    [Fact]
    public async Task DataProtection_PersistsKeysInTheCatalog()
    {
        await using var services = database.CreateServices();
        var protector = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Auxilia.Tenancy.ConnectionString.v1");

        var protectedValue = protector.Protect("Host=db;Database=tenant_acme");

        await using var other = database.CreateServices();
        other.GetRequiredService<IDataProtectionProvider>().CreateProtector("Auxilia.Tenancy.ConnectionString.v1")
            .Unprotect(protectedValue).ShouldBe("Host=db;Database=tenant_acme");
        await using var db = database.CreateContext();
        (await db.DataProtectionKeys.CountAsync(Ct)).ShouldBeGreaterThan(0);
    }

    private static Tenant NewTenant(string? slug = null) =>
        Tenant.Create(Guid.CreateVersion7(), slug ?? "t-" + Guid.NewGuid().ToString("N")[..10], "Acme", "it", "Europe/Rome").Value;
}
