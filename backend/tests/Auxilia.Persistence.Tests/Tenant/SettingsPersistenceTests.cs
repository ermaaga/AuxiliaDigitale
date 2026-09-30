using System.Text.Json;

using Auxilia.Application;
using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Configuration;
using Auxilia.Domain.Configuration;
using Auxilia.Domain.Platform;
using Auxilia.Infrastructure;
using Auxilia.Persistence.Tenant;
using Auxilia.Persistence.Tenant.Operations;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace Auxilia.Persistence.Tests.Tenant;

/// <summary>Tenant (level 3) and user (level 4) setting values in <c>configuration.*</c>, and the settings pipeline end to end.</summary>
[Collection(TenantDatabaseGroup.Name)]
public sealed class SettingsPersistenceTests(TenantDatabaseFixture database)
{
    private static readonly SettingDefinition<int> Persisted = new("test.persistedDays", "Test", 7, isValid: days => days > 0);

    private static readonly SettingDefinition<string> Theme = new("test.persistedTheme", "Test", "light", SettingScope.PlatformAndTenant | SettingScope.User);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TenantValues_AreInsertedUpdatedRemovedAndAudited()
    {
        await using var services = Services(ActorType.System);
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ITenantSettingStore>();
        var key = "test.k" + Guid.NewGuid().ToString("N")[..8];

        await store.SetTenantValueAsync(key, "7", Ct);
        await store.SetTenantValueAsync(key, "30", Ct);
        (await store.GetTenantValuesAsync(Ct))[key].ShouldBe("30");

        await store.RemoveTenantValueAsync(key, Ct);
        await store.RemoveTenantValueAsync(key, Ct);
        (await store.GetTenantValuesAsync(Ct)).ShouldNotContainKey(key);

        await using var db = database.CreateContext();
        var changes = await db.Set<EntityChange>().Where(change => change.EntityType == nameof(TenantSetting)).ToListAsync(Ct);
        var settingId = changes.Single(change => change.Action == "Created" && change.Changes.Contains(key, StringComparison.Ordinal)).EntityId;
        changes.Where(change => change.EntityId == settingId).Select(change => change.Action).ShouldBe(["Created", "Updated", "Deleted"], ignoreOrder: true);
    }

    [Fact]
    public async Task TenantKey_IsUnique()
    {
        await using var db = database.CreateContext();
        var key = "test.u" + Guid.NewGuid().ToString("N")[..8];
        db.Set<TenantSetting>().Add(new TenantSetting(Guid.CreateVersion7(), key, "1"));
        db.Set<TenantSetting>().Add(new TenantSetting(Guid.CreateVersion7(), key, "2"));

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task UserValues_BelongToOneUser()
    {
        await using var services = Services(ActorType.System);
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ITenantSettingStore>();
        var (anna, marco) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await store.SetUserValueAsync(anna, "ui.theme", "\"dark\"", Ct);
        await store.SetUserValueAsync(anna, "ui.theme", "\"blue\"", Ct);
        await store.SetUserValueAsync(marco, "ui.theme", "\"light\"", Ct);

        (await store.FindUserValueAsync(anna, "ui.theme", Ct)).ShouldBe("\"blue\"");
        (await store.FindUserValueAsync(marco, "ui.theme", Ct)).ShouldBe("\"light\"");

        await store.RemoveUserValueAsync(anna, "ui.theme", Ct);
        await store.RemoveUserValueAsync(anna, "ui.theme", Ct);
        (await store.FindUserValueAsync(anna, "ui.theme", Ct)).ShouldBeNull();
        (await store.FindUserValueAsync(marco, "ui.theme", Ct)).ShouldBe("\"light\"");
    }

    [Fact]
    public async Task Manager_WritesInTheOperationTransactionAndTheProviderSeesTheNewValueAtOnce()
    {
        await using var services = Services(ActorType.System);
        await using (var reset = services.CreateAsyncScope())
        {
            await reset.ServiceProvider.GetRequiredService<ISettingsManager>().ResetAsync(new ResetSetting(Persisted.Key, SettingScope.Tenant), Ct);
        }

        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISettingsProvider>();
        var manager = scope.ServiceProvider.GetRequiredService<ISettingsManager>();

        (await provider.GetAsync(Persisted, Ct)).ShouldBe(7);
        (await manager.SetAsync(new SetSetting(Persisted.Key, SettingScope.Tenant, JsonDocument.Parse("30").RootElement), Ct)).IsSuccess.ShouldBeTrue();

        // Same node, same scope: the snapshot was evicted after the commit.
        (await provider.GetAsync(Persisted, Ct)).ShouldBe(30);

        await using var other = services.CreateAsyncScope();
        (await other.ServiceProvider.GetRequiredService<ISettingsProvider>().GetAsync(Persisted, Ct)).ShouldBe(30);
    }

    [Fact]
    public async Task Provider_UserValueOverridesTenantValueForThatUserOnly()
    {
        await using var services = Services(ActorType.User);
        await using var scope = services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<ISettingsManager>();

        (await manager.SetAsync(new SetSetting(Theme.Key, SettingScope.User, JsonDocument.Parse("\"dark\"").RootElement), Ct)).IsSuccess.ShouldBeTrue();

        (await scope.ServiceProvider.GetRequiredService<ISettingsProvider>().GetAsync(Theme, Ct)).ShouldBe("dark");

        await using var system = Services(ActorType.System);
        await using var systemScope = system.CreateAsyncScope();
        (await systemScope.ServiceProvider.GetRequiredService<ISettingsProvider>().GetAsync(Theme, Ct)).ShouldBe("light");
    }

    private ServiceProvider Services(ActorType actor)
    {
        var tenant = new TenantInfo(Guid.Parse("0199a0b2-0000-7000-8000-0000000005e7"), "tenant-test", TenantStatus.Active, "it", "Europe/Rome");
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.Current.Returns(tenant);
        tenantContext.Tenant.Returns(tenant);
        tenantContext.IsResolved.Returns(true);
        var directory = Substitute.For<ITenantDirectory>();
        directory.GetProtectedConnectionStringAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns("protected");
        var protector = Substitute.For<ITenantConnectionProtector>();
        protector.Unprotect("protected").Returns(database.ConnectionString);
        var user = Substitute.For<ICurrentUser>();
        user.ActorType.Returns(actor);
        user.UserId.Returns(actor == ActorType.User ? Guid.Parse("0199a0b2-0000-7000-8000-0000000005e8") : null);
        var platform = Substitute.For<IPlatformSettingStore>();
        platform.GetValuesAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, string>());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure();
        services.AddScoped(_ => tenantContext);
        services.AddScoped(_ => directory);
        services.AddSingleton(protector);
        services.AddScoped(_ => user);
        services.AddScoped(_ => platform);
        services.AddSingleton(Substitute.For<ISettingSecretProtector>());
        services.AddApplication();
        services.AddSettingDefinitions([Persisted, Theme]);
        services.AddTenantPersistence();
        return services.BuildServiceProvider(validateScopes: true);
    }
}
