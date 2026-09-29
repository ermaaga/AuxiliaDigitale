using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Platform;
using Auxilia.Diagnostics;
using Auxilia.Domain.Platform;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Auxilia.Application.Tests.Platform;

public sealed class TenantMigrationManagerTests
{
    private readonly ICatalogStore catalog = Substitute.For<ICatalogStore>();
    private readonly ICatalogMigrator catalogMigrator = Substitute.For<ICatalogMigrator>();
    private readonly ITenantDatabaseAdmin databases = Substitute.For<ITenantDatabaseAdmin>();
    private readonly ITenantConnectionProtector protector = Substitute.For<ITenantConnectionProtector>();
    private readonly List<MigrationRun> runs = [];
    private readonly TenantMigrationManager manager;

    public TenantMigrationManagerTests()
    {
        catalog.When(store => store.Add(Arg.Any<MigrationRun>())).Do(call => runs.Add(call.Arg<MigrationRun>()));
        protector.Unprotect("protected").Returns("Host=db");
        databases.MigrateAsync("Host=db", false, Arg.Any<CancellationToken>()).Returns(new TenantDatabaseVersion("Tenant_Initial", "D_20260929_001"));

        manager = new TenantMigrationManager(
            ManagerHarness.Runner(), catalog, catalogMigrator, databases, protector, ManagerHarness.System(), TimeProvider.System);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MigrateCatalog_ReturnsAppliedMigrationsAndRecordsTheRun()
    {
        catalogMigrator.MigrateAsync(Arg.Any<CancellationToken>()).Returns(["Catalog_Initial"]);

        var result = await manager.MigrateCatalogAsync(Ct);

        result.Value.ShouldBe(["Catalog_Initial"]);
        runs.ShouldHaveSingleItem().Kind.ShouldBe(MigrationRunKind.CatalogSchema);
    }

    [Fact]
    public async Task MigrateTenant_FailedTenant_IsReactivatedWithVersions()
    {
        var tenant = TenantWith(TenantStatus.MigrationFailed);

        var result = await manager.MigrateTenantAsync("acme", Ct);

        result.Value.DataVersion.ShouldBe("D_20260929_001");
        tenant.Status.ShouldBe(TenantStatus.Active);
        tenant.SchemaVersion.ShouldBe("Tenant_Initial");
        runs.ShouldHaveSingleItem().Status.ShouldBe(MigrationRunStatus.Succeeded);
    }

    [Fact]
    public async Task MigrateTenant_Throws_ActiveBecomesMigrationFailed()
    {
        var tenant = TenantWith(TenantStatus.Active);
        databases.MigrateAsync("Host=db", false, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));

        await Should.ThrowAsync<InvalidOperationException>(() => manager.MigrateTenantAsync("acme", Ct));

        tenant.Status.ShouldBe(TenantStatus.MigrationFailed);
        runs.ShouldHaveSingleItem().Status.ShouldBe(MigrationRunStatus.Failed);
    }

    [Fact]
    public async Task MigrateTenant_Throws_SuspendedStaysSuspended()
    {
        var tenant = TenantWith(TenantStatus.Suspended);
        databases.MigrateAsync("Host=db", false, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));

        await Should.ThrowAsync<InvalidOperationException>(() => manager.MigrateTenantAsync("acme", Ct));

        tenant.Status.ShouldBe(TenantStatus.Suspended);
    }

    [Fact]
    public async Task MigrateTenant_ArchivedOrUnknown_IsNotFound()
    {
        TenantWith(TenantStatus.Archived);

        (await manager.MigrateTenantAsync("acme", Ct)).Error!.Code.ShouldBe(EventCodes.Tenancy.TenantNotFound);
        (await manager.MigrateTenantAsync("unknown", Ct)).Error!.Code.ShouldBe(EventCodes.Tenancy.TenantNotFound);
    }

    [Fact]
    public async Task MigrateTenant_Provisioning_IsNotAllowed()
    {
        TenantWith(TenantStatus.Provisioning);

        (await manager.MigrateTenantAsync("acme", Ct)).Error!.Code.ShouldBe(EventCodes.Tenancy.TenantTransitionNotAllowed);
    }

    [Fact]
    public async Task MigratableTenants_AreActiveSuspendedOrFailedWithADatabase()
    {
        catalog.ListTenantsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Build("b-active", TenantStatus.Active), Build("a-suspended", TenantStatus.Suspended),
            Build("c-failed", TenantStatus.MigrationFailed), Build("d-archived", TenantStatus.Archived),
            Build("e-provisioning", TenantStatus.Provisioning),
        ]);

        (await manager.MigratableTenantsAsync(Ct)).ShouldBe(["a-suspended", "b-active", "c-failed"]);
    }

    private Tenant TenantWith(TenantStatus status)
    {
        var tenant = Build("acme", status);
        catalog.FindTenantAsync("acme", Arg.Any<CancellationToken>()).Returns(tenant);
        return tenant;
    }

    private static Tenant Build(string slug, TenantStatus status)
    {
        var tenant = Tenant.Create(Guid.CreateVersion7(), slug, slug, "it", "Europe/Rome").Value;
        if (status != TenantStatus.Provisioning)
        {
            tenant.SetConnectionSecret("protected");
        }

        switch (status)
        {
            case TenantStatus.Active:
                tenant.Activate();
                break;
            case TenantStatus.Suspended:
                tenant.Activate();
                tenant.Suspend();
                break;
            case TenantStatus.MigrationFailed:
                tenant.MarkMigrationFailed();
                break;
            case TenantStatus.Archived:
                tenant.Archive(DateTimeOffset.UtcNow);
                break;
        }

        return tenant;
    }
}
