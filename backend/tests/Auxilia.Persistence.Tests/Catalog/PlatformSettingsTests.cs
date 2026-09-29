using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Persistence.Tests.Catalog;

/// <summary>Platform setting values (level 2) and the protection of secret settings with the Catalog key ring.</summary>
[Collection(CatalogDatabaseGroup.Name)]
public sealed class PlatformSettingsTests(CatalogDatabaseFixture database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Values_AreInsertedUpdatedAndRemovedWithAuditColumns()
    {
        await using var services = database.CreateServices();
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IPlatformSettingStore>();
        var key = "test.p" + Guid.NewGuid().ToString("N")[..8];

        await store.SetAsync(key, "true", Ct);
        await store.SetAsync(key, "false", Ct);
        (await store.GetValuesAsync(Ct))[key].ShouldBe("false");

        await using (var db = database.CreateContext())
        {
            var stored = await db.PlatformSettings.SingleAsync(setting => setting.Key == key, Ct);
            db.Entry(stored).Property<string?>("UpdatedBy").CurrentValue.ShouldBe("platform:" + CatalogDatabaseFixture.ActorId);
        }

        await store.RemoveAsync(key, Ct);
        await store.RemoveAsync(key, Ct);
        (await store.GetValuesAsync(Ct)).ShouldNotContainKey(key);
    }

    [Fact]
    public async Task SecretProtector_RoundTripsAcrossProvidersAndIsolatesItsPurpose()
    {
        string protectedValue;
        string protectedConnection;
        await using (var first = database.CreateServices())
        {
            protectedValue = first.GetRequiredService<ISettingSecretProtector>().Protect("smtp-password");
            protectedConnection = first.GetRequiredService<ITenantConnectionProtector>().Protect("Host=db");
        }

        await using var second = database.CreateServices();
        var secrets = second.GetRequiredService<ISettingSecretProtector>();

        protectedValue.ShouldNotContain("smtp-password");
        secrets.Unprotect(protectedValue).ShouldBe("smtp-password");
        Should.Throw<System.Security.Cryptography.CryptographicException>(() => secrets.Unprotect(protectedConnection));
    }

    [Fact]
    public async Task TenantDirectory_ServesLookupsFromTheReferenceDataCache()
    {
        await using var services = database.CreateServices(configure => configure.AddInfrastructure());
        await using var scope = services.CreateAsyncScope();

        (await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().FindBySlugAsync("no-such-tenant", Ct)).ShouldBeNull();
        (await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().FindByHostAsync("unknown.example.com", Ct)).ShouldBeNull();
        (await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().FindBySlugAsync("../etc", Ct)).ShouldBeNull();
    }
}
