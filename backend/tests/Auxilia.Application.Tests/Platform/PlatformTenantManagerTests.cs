using Auxilia.Application.Abstractions.Caching;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Bus;
using Auxilia.Application.Platform;
using Auxilia.Application.Tests.Identity;
using Auxilia.Contracts.Messages.V1.Platform;
using Auxilia.Contracts.Platform;
using Auxilia.Diagnostics;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Auxilia.Application.Tests.Platform;

public sealed class PlatformTenantManagerTests
{
    private static readonly Guid Premium = Guid.CreateVersion7();

    private readonly ICatalogStore catalog = Substitute.For<ICatalogStore>();
    private readonly IMessageSender sender = Substitute.For<IMessageSender>();
    private readonly IReferenceDataCache cache = Substitute.For<IReferenceDataCache>();
    private readonly ManualTimeProvider clock = new();
    private readonly List<Tenant> tenants = [];
    private readonly List<TenantPlan> periods = [];
    private readonly List<TenantModuleOverride> overrides = [];
    private readonly List<object> sent = [];
    private readonly PlatformTenantManager manager;

    public PlatformTenantManagerTests()
    {
        catalog.FindTenantAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => tenants.SingleOrDefault(tenant => tenant.Slug == call.Arg<string>()));
        catalog.When(store => store.Add(Arg.Any<Tenant>())).Do(call => tenants.Add(call.Arg<Tenant>()));
        catalog.When(store => store.Add(Arg.Any<TenantPlan>())).Do(call => periods.Add(call.Arg<TenantPlan>()));
        catalog.DefaultPlanIdAsync(Arg.Any<CancellationToken>()).Returns(Plan.StandardId);
        catalog.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Plan.Create(Plan.StandardId, Plan.StandardCode, "plans.standard", isDefault: true).Value,
            Plan.Create(Premium, "premium", "plans.premium", isDefault: false).Value,
        ]);
        catalog.CurrentTenantPlanAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call => periods.LastOrDefault(period => period.TenantId == call.Arg<Guid>() && period.IsValidAt(call.Arg<DateTimeOffset>())));
        catalog.ListModulesAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new PlatformModule("identity", ModuleKind.Core, "modules.identity", 12000),
            new PlatformModule("cases", ModuleKind.Optional, "modules.cases", 14000),
        ]);
        catalog.ListOverridesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => overrides.Where(item => item.TenantId == call.Arg<Guid>()).ToArray());
        catalog.When(store => store.Add(Arg.Any<TenantModuleOverride>())).Do(call => overrides.Add(call.Arg<TenantModuleOverride>()));
        catalog.When(store => store.Remove(Arg.Any<TenantModuleOverride>())).Do(call => overrides.Remove(call.Arg<TenantModuleOverride>()));
        sender.When(bus => bus.SendAsync(Arg.Any<object>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>()))
            .Do(call => sent.Add(call.Arg<object>()));

        var tenantContext = Substitute.For<ITenantContext>();
        var headers = new OutgoingMessageHeaders(tenantContext, ManagerHarness.System(), Substitute.For<ICorrelationContext>());
        manager = new PlatformTenantManager(
            ManagerHarness.Runner(), catalog, sender, headers, cache,
            new CreatePlatformTenantRequestValidator(), new UpdatePlatformTenantRequestValidator(), clock,
            NullLogger<PlatformTenantManager>.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CreatePlatformTenantRequest Request(string slug = "acme", TenantAdministratorInvite? administrator = null) =>
        new(slug, "ACME S.r.l.", "it", "Europe/Rome", administrator);

    private Tenant Existing(TenantStatus status = TenantStatus.Active)
    {
        var tenant = Tenant.Create(Guid.CreateVersion7(), "acme", "Acme", "it", "Europe/Rome").Value;
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
                tenant.Archive(DateTimeOffset.UnixEpoch);
                break;
        }

        tenants.Add(tenant);
        periods.Add(new TenantPlan(Guid.CreateVersion7(), tenant.Id, Plan.StandardId, DateTimeOffset.UnixEpoch));
        return tenant;
    }

    [Fact]
    public async Task Create_AddsTheTenantWithTheDefaultPlan_AndQueuesProvisioningAfterCommit()
    {
        var result = await manager.CreateAsync(Request(administrator: new TenantAdministratorInvite(" anna@acme.test ", "Anna", "Bianchi")), Ct);

        result.IsSuccess.ShouldBeTrue();
        var tenant = tenants.ShouldHaveSingleItem();
        (tenant.Slug, tenant.Status, tenant.DisplayName).ShouldBe(("acme", TenantStatus.Provisioning, "ACME S.r.l."));
        periods.ShouldHaveSingleItem().PlanId.ShouldBe(Plan.StandardId);
        var command = sent.ShouldHaveSingleItem().ShouldBeOfType<ProvisionTenantCommand>();
        command.ShouldBe(new ProvisionTenantCommand("acme", new ProvisionTenantAdministrator("anna@acme.test", "Anna", "Bianchi")));
        await cache.Received(1).InvalidateAsync(CacheTags.CatalogTenants, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_WhenTheBusIsDown_StillCreatesTheTenant_ForARetryFromTheConsole()
    {
        sender.SendAsync(Arg.Any<object>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("RabbitMQ is not configured"));

        (await manager.CreateAsync(Request(), Ct)).IsSuccess.ShouldBeTrue();

        tenants.ShouldHaveSingleItem().Status.ShouldBe(TenantStatus.Provisioning);
    }

    [Theory]
    [InlineData("Acme", "it", "Europe/Rome", "slug")]
    [InlineData("api", "it", "Europe/Rome", "slug")]
    [InlineData("acme", "fr", "Europe/Rome", "defaultLanguage")]
    [InlineData("acme", "it", "Mars/Olympus", "timeZone")]
    public async Task Create_InvalidValues_AreFieldErrors(string slug, string language, string timeZone, string field)
    {
        var result = await manager.CreateAsync(new CreatePlatformTenantRequest(slug, "Acme", language, timeZone, null), Ct);

        result.Error!.ValidationErrors.Keys.ShouldContain(field);
        tenants.ShouldBeEmpty();
        sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Create_InvalidAdministrator_IsAFieldErrorOnTheAdministrator()
    {
        var result = await manager.CreateAsync(Request(administrator: new TenantAdministratorInvite("not-an-email", "", "Bianchi")), Ct);

        result.Error!.ValidationErrors.Keys.ShouldBe(["administrator.email", "administrator.firstName"], ignoreOrder: true);
    }

    [Fact]
    public async Task Create_ExistingSlug_IsAConflict()
    {
        Existing();

        (await manager.CreateAsync(Request(), Ct)).Error!.Code.ShouldBe(EventCodes.Tenancy.TenantAlreadyExists);
        sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task RetryProvisioning_OnlyForTenantsWaitingForIt()
    {
        Existing(TenantStatus.MigrationFailed);

        (await manager.RetryProvisioningAsync("acme", Ct)).IsSuccess.ShouldBeTrue();
        sent.ShouldHaveSingleItem().ShouldBe(new ProvisionTenantCommand("acme", null));

        tenants.Single().Activate();
        (await manager.RetryProvisioningAsync("acme", Ct)).Error!.Code.ShouldBe(EventCodes.Tenancy.TenantNotProvisioning);
        (await manager.RetryProvisioningAsync("unknown", Ct)).Error!.Code.ShouldBe(EventCodes.Tenancy.TenantNotFound);
    }

    [Fact]
    public async Task Update_ChangesNameAndTimeZone_AndArchivedTenantsAreReadOnly()
    {
        var tenant = Existing();

        (await manager.UpdateAsync("acme", new UpdatePlatformTenantRequest(" ACME Group ", "Europe/London"), Ct)).IsSuccess.ShouldBeTrue();
        (tenant.DisplayName, tenant.TimeZone).ShouldBe(("ACME Group", "Europe/London"));
        (await manager.UpdateAsync("acme", new UpdatePlatformTenantRequest("", "Nowhere/Else"), Ct)).Error!.ValidationErrors.Keys
            .ShouldBe(["displayName", "timeZone"], ignoreOrder: true);

        tenant.Archive(clock.GetUtcNow());
        (await manager.UpdateAsync("acme", new UpdatePlatformTenantRequest("New", "Europe/Rome"), Ct)).Error!.Code
            .ShouldBe(EventCodes.Tenancy.TenantArchived);
    }

    [Fact]
    public async Task StatusChanges_FollowTheTenantStateMachine()
    {
        var tenant = Existing();

        (await manager.ReactivateAsync("acme", Ct)).Error!.Code.ShouldBe(EventCodes.Tenancy.TenantTransitionNotAllowed);
        (await manager.SuspendAsync("acme", Ct)).IsSuccess.ShouldBeTrue();
        (await manager.ReactivateAsync("acme", Ct)).IsSuccess.ShouldBeTrue();
        (await manager.ArchiveAsync("acme", Ct)).IsSuccess.ShouldBeTrue();
        (tenant.Status, tenant.ArchivedAt).ShouldBe((TenantStatus.Archived, clock.GetUtcNow()));
        (await manager.SuspendAsync("acme", Ct)).Error!.Code.ShouldBe(EventCodes.Tenancy.TenantArchived);
        (await manager.SuspendAsync("unknown", Ct)).Error!.Code.ShouldBe(EventCodes.Tenancy.TenantNotFound);

        // One invalidation of the tenant lookups per successful change, none for the refused ones.
        await cache.Received(3).InvalidateAsync(CacheTags.CatalogTenants, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangePlan_EndsTheCurrentPeriodAndStartsTheNewPlanNow()
    {
        var tenant = Existing();

        (await manager.ChangePlanAsync("acme", new ChangeTenantPlanRequest("premium"), Ct)).IsSuccess.ShouldBeTrue();

        periods.Count.ShouldBe(2);
        periods[0].ValidTo.ShouldBe(clock.GetUtcNow());
        (periods[1].PlanId, periods[1].ValidFrom, periods[1].TenantId).ShouldBe((Premium, clock.GetUtcNow(), tenant.Id));
        await cache.Received(1).InvalidateAsync(CacheTags.Tenant("acme", "platform"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangePlan_SamePlanChangesNothing_AndUnknownPlanIsNotFound()
    {
        Existing();

        (await manager.ChangePlanAsync("acme", new ChangeTenantPlanRequest(Plan.StandardCode), Ct)).IsSuccess.ShouldBeTrue();
        (await manager.ChangePlanAsync("acme", new ChangeTenantPlanRequest("gold"), Ct)).Error!.Code.ShouldBe(EventCodes.Tenancy.PlanNotFound);
        periods.ShouldHaveSingleItem().ValidTo.ShouldBeNull();
    }

    [Fact]
    public async Task ModuleOverride_IsSetReplacedAndRemoved_AndRefreshesTheTenantModules()
    {
        Existing();

        (await manager.SetModuleOverrideAsync("acme", "cases", new SetModuleOverrideRequest(true, ["Employee", "Administrator"]), Ct)).IsSuccess.ShouldBeTrue();
        overrides.ShouldHaveSingleItem().Roles.ShouldBe([TenantRole.Administrator, TenantRole.Employee]);

        (await manager.SetModuleOverrideAsync("acme", "cases", new SetModuleOverrideRequest(false, []), Ct)).IsSuccess.ShouldBeTrue();
        (overrides.Single().IsEnabled, overrides.Single().Roles.Length).ShouldBe((false, 0));

        (await manager.RemoveModuleOverrideAsync("acme", "cases", Ct)).IsSuccess.ShouldBeTrue();
        (await manager.RemoveModuleOverrideAsync("acme", "cases", Ct)).IsSuccess.ShouldBeTrue();
        overrides.ShouldBeEmpty();
        await cache.Received(3).InvalidateAsync(CacheTags.Tenant("acme", "platform"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ModuleOverride_RefusesCoreUnknownModulesAndInvalidRoles()
    {
        Existing();

        (await manager.SetModuleOverrideAsync("acme", "identity", new SetModuleOverrideRequest(false, []), Ct)).Error!.Code
            .ShouldBe(EventCodes.Tenancy.CoreModuleNotConfigurable);
        (await manager.SetModuleOverrideAsync("acme", "unknown", new SetModuleOverrideRequest(false, []), Ct)).Error!.Code
            .ShouldBe(EventCodes.Tenancy.ModuleNotFound);
        (await manager.SetModuleOverrideAsync("acme", "cases", new SetModuleOverrideRequest(true, []), Ct)).Error!.ValidationErrors.Keys
            .ShouldBe(["roles"]);
        (await manager.SetModuleOverrideAsync("acme", "cases", new SetModuleOverrideRequest(true, ["System"]), Ct)).Error!.ValidationErrors.Keys
            .ShouldBe(["roles"]);
        overrides.ShouldBeEmpty();
    }
}
