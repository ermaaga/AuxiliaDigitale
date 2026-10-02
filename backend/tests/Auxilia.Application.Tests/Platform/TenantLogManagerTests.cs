using Auxilia.Application.Abstractions.Logging;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Platform;
using Auxilia.Application.Tests.Identity;
using Auxilia.Contracts.Platform;
using Auxilia.Diagnostics;
using Auxilia.Diagnostics.Logging;
using Auxilia.Domain.Platform;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace Auxilia.Application.Tests.Platform;

public sealed class TenantLogManagerTests
{
    private readonly ICatalogStore catalog = Substitute.For<ICatalogStore>();
    private readonly ITenantLogLevels levels = Substitute.For<ITenantLogLevels>();
    private readonly ITenantLogLevelBroadcast broadcast = Substitute.For<ITenantLogLevelBroadcast>();
    private readonly ManualTimeProvider clock = new();
    private readonly List<Tenant> tenants = [];
    private readonly TenantLogManager manager;
    private readonly TenantLogLevelSync sync;

    public TenantLogManagerTests()
    {
        catalog.FindTenantAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => tenants.SingleOrDefault(tenant => tenant.Slug == call.Arg<string>()));
        catalog.ListTenantsAsync(Arg.Any<CancellationToken>()).Returns(_ => tenants.ToArray());
        manager = new TenantLogManager(ManagerHarness.Runner(), catalog, levels, broadcast, clock, NullLogger<TenantLogManager>.Instance);
        sync = new TenantLogLevelSync(catalog, levels, clock);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => clock.GetUtcNow();

    private Tenant Add(string slug = "acme")
    {
        var tenant = Tenant.Create(Guid.CreateVersion7(), slug, "Acme", "it", "Europe/Rome").Value;
        tenant.Activate();
        tenants.Add(tenant);
        return tenant;
    }

    [Fact]
    public async Task EnableDebugAsync_StoresAppliesAndAnnouncesTheEnd()
    {
        var tenant = Add();

        var result = await manager.EnableDebugAsync("acme", new EnableTenantDebugLoggingRequest(Now.AddHours(1)), Ct);

        result.IsSuccess.ShouldBeTrue();
        tenant.DebugLoggingUntil.ShouldBe(Now.AddHours(1));
        await catalog.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        levels.Received(1).EnableDebug("acme", Now.AddHours(1));
        await broadcast.Received(1).PublishAsync("acme", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnableDebugAsync_OffsetInstant_IsStoredInUtc()
    {
        var tenant = Add();
        var until = Now.AddHours(1).ToOffset(TimeSpan.FromHours(2));

        (await manager.EnableDebugAsync("acme", new EnableTenantDebugLoggingRequest(until), Ct)).IsSuccess.ShouldBeTrue();

        tenant.DebugLoggingUntil!.Value.Offset.ShouldBe(TimeSpan.Zero);
        tenant.DebugLoggingUntil.ShouldBe(until);
    }

    [Fact]
    public async Task EnableDebugAsync_TooFarAhead_IsRejectedAndNothingChanges()
    {
        Add();

        var result = await manager.EnableDebugAsync("acme", new EnableTenantDebugLoggingRequest(Now.AddHours(25)), Ct);

        result.Error!.Code.ShouldBe(EventCodes.Tenancy.LogLevelUntilInvalid);
        await catalog.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        levels.DidNotReceiveWithAnyArgs().EnableDebug(default!, default);
        await broadcast.DidNotReceiveWithAnyArgs().PublishAsync(default!, Ct);
    }

    [Fact]
    public async Task EnableDebugAsync_UnknownTenant_ReturnsNotFound()
    {
        var result = await manager.EnableDebugAsync("ghost", new EnableTenantDebugLoggingRequest(Now.AddHours(1)), Ct);

        result.Error!.Code.ShouldBe(EventCodes.Tenancy.TenantNotFound);
    }

    [Fact]
    public async Task DisableDebugAsync_ClearsAndAnnounces()
    {
        var tenant = Add();
        tenant.EnableDebugLogging(Now.AddHours(1), Now);

        (await manager.DisableDebugAsync("acme", Ct)).IsSuccess.ShouldBeTrue();

        tenant.DebugLoggingUntil.ShouldBeNull();
        levels.Received(1).Clear("acme");
        await broadcast.Received(1).PublishAsync("acme", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisableDebugAsync_NotEnabled_SucceedsWithoutWriting()
    {
        Add();

        (await manager.DisableDebugAsync("acme", Ct)).IsSuccess.ShouldBeTrue();

        await catalog.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await broadcast.DidNotReceiveWithAnyArgs().PublishAsync(default!, Ct);
    }

    [Fact]
    public async Task LoadAllAsync_AppliesRunningOverridesAndClearsTheOthers()
    {
        Add("acme").EnableDebugLogging(Now.AddHours(1), Now);
        var expired = Add("beta");
        expired.EnableDebugLogging(Now.AddMinutes(10), Now);
        Add("gamma");
        clock.Advance(TimeSpan.FromMinutes(20));

        await sync.LoadAllAsync(Ct);

        levels.Received(1).EnableDebug("acme", Arg.Any<DateTimeOffset>());
        levels.Received(1).Clear("beta");
        levels.Received(1).Clear("gamma");
    }

    [Fact]
    public async Task RefreshAsync_ReadsTheCatalog_AndClearsUnknownTenants()
    {
        var tenant = Add();
        tenant.EnableDebugLogging(Now.AddHours(1), Now);

        await sync.RefreshAsync("acme", Ct);
        await sync.RefreshAsync("ghost", Ct);

        levels.Received(1).EnableDebug("acme", Now.AddHours(1));
        levels.Received(1).Clear("ghost");
    }
}
