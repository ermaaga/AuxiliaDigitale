using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Platform;
using Auxilia.Diagnostics;
using Auxilia.Domain.Platform;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Auxilia.Application.Tests.Platform;

public sealed class TenantLifecycleManagerTests
{
    private const string Plain = "Host=db;Database=auxilia_t_acme;Username=auxilia_t_acme;Password=secret";

    private readonly ICatalogStore catalog = Substitute.For<ICatalogStore>();
    private readonly ITenantDatabaseAdmin databases = Substitute.For<ITenantDatabaseAdmin>();
    private readonly ITenantConnectionProtector protector = Substitute.For<ITenantConnectionProtector>();
    private readonly List<MigrationRun> runs = [];
    private readonly List<TenantPlan> plans = [];
    private readonly TenantLifecycleManager manager;

    public TenantLifecycleManagerTests()
    {
        catalog.When(store => store.Add(Arg.Any<MigrationRun>())).Do(call => runs.Add(call.Arg<MigrationRun>()));
        catalog.When(store => store.Add(Arg.Any<TenantPlan>())).Do(call => plans.Add(call.Arg<TenantPlan>()));
        catalog.DefaultPlanIdAsync(Arg.Any<CancellationToken>()).Returns(Plan.StandardId);
        protector.Protect(Arg.Any<string>()).Returns(call => "protected:" + call.Arg<string>().Length);
        protector.Unprotect(Arg.Any<string>()).Returns(Plain);
        databases.CreateDatabaseAsync("acme", Arg.Any<CancellationToken>()).Returns(Plain);
        databases.MigrateAsync(Plain, true, Arg.Any<CancellationToken>()).Returns(new TenantDatabaseVersion("Tenant_Initial", null));

        manager = new TenantLifecycleManager(ManagerHarness.Runner(), catalog, databases, protector, ManagerHarness.System(), TimeProvider.System);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Provision_NewTenant_CreatesDatabaseStoresProtectedSecretAndActivates()
    {
        Tenant? added = null;
        catalog.When(store => store.Add(Arg.Any<Tenant>())).Do(call => added = call.Arg<Tenant>());

        var result = await manager.ProvisionAsync(new ProvisionTenant("acme", "Acme", "it", "Europe/Rome"), Ct);

        result.Value.Status.ShouldBe(TenantStatus.Active);
        added!.ConnectionSecret.ShouldBe("protected:" + Plain.Length);
        added.SchemaVersion.ShouldBe("Tenant_Initial");
        plans.ShouldHaveSingleItem().PlanId.ShouldBe(Plan.StandardId);
        runs.ShouldHaveSingleItem().Status.ShouldBe(MigrationRunStatus.Succeeded);
    }

    [Fact]
    public async Task Provision_ExistingActiveTenant_IsConflict()
    {
        var tenant = Tenant.Create(Guid.CreateVersion7(), "acme", "Acme", "it", "Europe/Rome").Value;
        tenant.Activate();
        catalog.FindTenantAsync("acme", Arg.Any<CancellationToken>()).Returns(tenant);

        var result = await manager.ProvisionAsync(new ProvisionTenant("acme", "Acme", "it", "Europe/Rome"), Ct);

        result.Error!.Code.ShouldBe(EventCodes.Tenancy.TenantAlreadyExists);
        await databases.DidNotReceive().CreateDatabaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Provision_ResumedTenantWithSecret_ReusesItsDatabase()
    {
        var tenant = Tenant.Create(Guid.CreateVersion7(), "acme", "Acme", "it", "Europe/Rome").Value;
        tenant.SetConnectionSecret("protected");
        tenant.MarkMigrationFailed();
        catalog.FindTenantAsync("acme", Arg.Any<CancellationToken>()).Returns(tenant);

        var result = await manager.ProvisionAsync(new ProvisionTenant("acme", "Acme", "it", "Europe/Rome"), Ct);

        result.IsSuccess.ShouldBeTrue();
        await databases.DidNotReceive().CreateDatabaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        plans.ShouldBeEmpty();
    }

    [Fact]
    public async Task Provision_InvalidSlug_ReturnsValidationError()
    {
        var result = await manager.ProvisionAsync(new ProvisionTenant("A!", "Acme", "it", "Europe/Rome"), Ct);

        result.Error!.Code.ShouldBe(EventCodes.Tenancy.TenantSlugInvalid);
    }

    [Fact]
    public async Task Provision_ExistingDatabaseUnreachable_FailsTheRun()
    {
        databases.CanConnectAsync("Host=dba", Arg.Any<CancellationToken>()).Returns(false);

        var result = await manager.ProvisionAsync(new ProvisionTenant("acme", "Acme", "it", "Europe/Rome", "Host=dba"), Ct);

        result.Error!.Code.ShouldBe(EventCodes.Tenancy.TenantDatabaseInvalid);
        runs.ShouldHaveSingleItem().Status.ShouldBe(MigrationRunStatus.Failed);
    }

    [Fact]
    public async Task Provision_MigrationThrows_MarksMigrationFailedAndRethrows()
    {
        Tenant? added = null;
        catalog.When(store => store.Add(Arg.Any<Tenant>())).Do(call => added = call.Arg<Tenant>());
        databases.MigrateAsync(Plain, true, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));

        await Should.ThrowAsync<InvalidOperationException>(() => manager.ProvisionAsync(new ProvisionTenant("acme", "Acme", "it", "Europe/Rome"), Ct));

        added!.Status.ShouldBe(TenantStatus.MigrationFailed);
        runs.ShouldHaveSingleItem().ErrorCode.ShouldBe($"AUX-{EventCodes.Runner.TenantMigrationFailed}");
    }

    [Fact]
    public async Task StatusChanges_FollowTheTenantStateMachine()
    {
        var tenant = Tenant.Create(Guid.CreateVersion7(), "acme", "Acme", "it", "Europe/Rome").Value;
        tenant.Activate();
        catalog.FindTenantAsync("acme", Arg.Any<CancellationToken>()).Returns(tenant);

        (await manager.ReactivateAsync("acme", Ct)).Error!.Code.ShouldBe(EventCodes.Tenancy.TenantTransitionNotAllowed);
        (await manager.SuspendAsync("acme", Ct)).IsSuccess.ShouldBeTrue();
        (await manager.ReactivateAsync("acme", Ct)).IsSuccess.ShouldBeTrue();
        (await manager.ArchiveAsync("acme", Ct)).IsSuccess.ShouldBeTrue();
        tenant.Status.ShouldBe(TenantStatus.Archived);
        (await manager.SuspendAsync("unknown", Ct)).Error!.Code.ShouldBe(EventCodes.Tenancy.TenantNotFound);
    }
}
