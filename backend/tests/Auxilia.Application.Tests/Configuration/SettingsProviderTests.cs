using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Caching;
using Auxilia.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Tests.Configuration;

public sealed class SettingsProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetAsync_NothingStored_ReturnsTheCodeDefault()
    {
        var harness = new SettingsHarness(SettingsHarness.Acme);

        (await harness.Provider.GetAsync(SettingsHarness.Days, Ct)).ShouldBe(7);
    }

    [Fact]
    public async Task GetAsync_ResolvesUserThenTenantThenPlatform()
    {
        var harness = new SettingsHarness(SettingsHarness.Acme, ActorType.User);
        harness.Stores.Platform["test.theme"] = "\"platform\"";

        (await harness.Provider.GetAsync(SettingsHarness.Theme, Ct)).ShouldBe("platform");

        harness.Stores.Tenant["test.theme"] = "\"tenant\"";
        await harness.Snapshots.InvalidateTenantAsync("acme", Ct);
        (await harness.Provider.GetAsync(SettingsHarness.Theme, Ct)).ShouldBe("tenant");

        harness.Stores.User[(SettingsHarness.UserId, "test.theme")] = "\"dark\"";
        (await harness.Provider.GetAsync(SettingsHarness.Theme, Ct)).ShouldBe("dark");
    }

    [Fact]
    public async Task GetAsync_SkipsLevelsTheDefinitionDoesNotAllow()
    {
        var harness = new SettingsHarness(SettingsHarness.Acme, ActorType.User);
        harness.Stores.Tenant["test.platformOnly"] = "5";
        harness.Stores.User[(SettingsHarness.UserId, "test.days")] = "30";

        (await harness.Provider.GetAsync(SettingsHarness.PlatformOnly, Ct)).ShouldBe(1);
        (await harness.Provider.GetAsync(SettingsHarness.Days, Ct)).ShouldBe(7);
    }

    [Fact]
    public async Task GetAsync_WithoutTenant_UsesPlatformValuesAndIgnoresUserValues()
    {
        var harness = new SettingsHarness(tenant: null, ActorType.User);
        harness.Stores.Platform["test.theme"] = "\"platform\"";
        harness.Stores.Tenant["test.theme"] = "\"tenant\"";
        harness.Stores.User[(SettingsHarness.UserId, "test.theme")] = "\"dark\"";

        (await harness.Provider.GetAsync(SettingsHarness.Theme, Ct)).ShouldBe("platform");
        harness.Cache.Requests.ShouldAllBe(entry => entry.Key == "platform:configuration:snapshot:current");
    }

    [Fact]
    public async Task GetAsync_InvalidStoredValue_IsIgnoredWithAWarning()
    {
        var harness = new SettingsHarness(SettingsHarness.Acme, ActorType.User);
        harness.Stores.Platform["test.days"] = "30";
        harness.Stores.Tenant["test.days"] = "-1";
        harness.Stores.User[(SettingsHarness.UserId, "test.theme")] = "42";

        (await harness.Provider.GetAsync(SettingsHarness.Days, Ct)).ShouldBe(30);
        (await harness.Provider.GetAsync(SettingsHarness.Theme, Ct)).ShouldBe("light");

        var warnings = harness.ProviderLogger.Entries.Where(entry => entry.EventId.Id == EventCodes.Configuration.StoredSettingIgnored).ToArray();
        warnings.Select(entry => entry.Properties["SettingLevel"]).ShouldBe(["Tenant", "User"]);
        warnings.ShouldAllBe(entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task GetAsync_ReadsTheSnapshotOncePerTenant()
    {
        var harness = new SettingsHarness(SettingsHarness.Acme);

        await harness.Provider.GetAsync(SettingsHarness.Days, Ct);
        await harness.Provider.GetAsync(SettingsHarness.Enabled, Ct);

        harness.Cache.Loads.ShouldBe(1);
        var entry = harness.Cache.Requests[0];
        entry.Key.ShouldBe("t:acme:configuration:snapshot:current");
        entry.Tags.ShouldBe(["t:acme:configuration", "platform:configuration"]);
        entry.LocalExpiration.ShouldBe(TimeSpan.FromSeconds(60));
        entry.Expiration.ShouldBe(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task GetSecretAsync_DecryptsTheStoredValue()
    {
        var harness = new SettingsHarness(SettingsHarness.Acme);

        (await harness.Provider.GetSecretAsync(SettingsHarness.Password, Ct)).ShouldBeNull();

        harness.Stores.Platform["test.password"] = "\"enc:platform-secret\"";
        harness.Stores.Tenant["test.password"] = "\"garbage\"";
        await harness.Snapshots.InvalidatePlatformAsync(Ct);

        (await harness.Provider.GetSecretAsync(SettingsHarness.Password, Ct)).ShouldBe("platform-secret");
        harness.ProviderLogger.Entries.ShouldHaveSingleItem().EventId.Id.ShouldBe(EventCodes.Configuration.StoredSettingIgnored);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("\"\"")]
    [InlineData("{")]
    public async Task GetSecretAsync_UnreadableValue_FallsBackToNull(string json)
    {
        var harness = new SettingsHarness(SettingsHarness.Acme);
        harness.Stores.Tenant["test.password"] = json;

        (await harness.Provider.GetSecretAsync(SettingsHarness.Password, Ct)).ShouldBeNull();
    }

    [Fact]
    public void CacheKeys_FollowTheDocumentedFormat()
    {
        CacheKeys.Tenant("acme", "localization", "bundle", "it").ShouldBe("t:acme:localization:bundle:it");
        CacheKeys.Platform("configuration", "snapshot", "current").ShouldBe("platform:configuration:snapshot:current");
        CacheKeys.Catalog("tenant", "slug:acme").ShouldBe("catalog:tenant:slug:acme");
        CacheTags.Tenant("acme", "configuration").ShouldBe("t:acme:configuration");
        CacheTags.Platform("configuration").ShouldBe("platform:configuration");
    }
}
