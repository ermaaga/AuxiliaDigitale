using System.Text.Json;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Configuration;
using Auxilia.Diagnostics;

namespace Auxilia.Application.Tests.Configuration;

public sealed class SettingsManagerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SetAsync_TenantLevel_StoresCanonicalJsonAndInvalidatesTheTenantAfterCommit()
    {
        var harness = new SettingsHarness(SettingsHarness.Acme);
        (await harness.Provider.GetAsync(SettingsHarness.Days, Ct)).ShouldBe(7);

        var result = await harness.Manager.SetAsync(new SetSetting("test.days", SettingScope.Tenant, Json("30")), Ct);

        result.IsSuccess.ShouldBeTrue();
        harness.Stores.Tenant["test.days"].ShouldBe("30");
        harness.Cache.Invalidated.ShouldBe(["t:acme:configuration"]);
        (await harness.Provider.GetAsync(SettingsHarness.Days, Ct)).ShouldBe(30);
    }

    [Fact]
    public async Task SetAsync_PlatformLevel_WorksWithoutTenantAndInvalidatesEveryTenant()
    {
        var harness = new SettingsHarness();

        (await harness.Manager.SetAsync(new SetSetting("test.enabled", SettingScope.Platform, Json("true")), Ct)).IsSuccess.ShouldBeTrue();

        harness.Stores.Platform["test.enabled"].ShouldBe("true");
        harness.Cache.Invalidated.ShouldBe(["platform:configuration"]);
    }

    [Fact]
    public async Task SetAsync_UserLevel_StoresForTheCurrentUserWithoutCacheInvalidation()
    {
        var harness = new SettingsHarness(SettingsHarness.Acme, ActorType.User);

        (await harness.Manager.SetAsync(new SetSetting("test.theme", SettingScope.User, Json("\"dark\"")), Ct)).IsSuccess.ShouldBeTrue();

        harness.Stores.User[(SettingsHarness.UserId, "test.theme")].ShouldBe("\"dark\"");
        harness.Cache.Invalidated.ShouldBeEmpty();
        (await harness.Provider.GetAsync(SettingsHarness.Theme, Ct)).ShouldBe("dark");
    }

    [Fact]
    public async Task SetAsync_Secret_IsStoredProtectedAndReadBackDecrypted()
    {
        var harness = new SettingsHarness(SettingsHarness.Acme);

        (await harness.Manager.SetAsync(new SetSetting("test.password", SettingScope.Tenant, Json("\"s3cret\"")), Ct)).IsSuccess.ShouldBeTrue();

        harness.Stores.Tenant["test.password"].ShouldBe("\"enc:s3cret\"");
        (await harness.Provider.GetSecretAsync(SettingsHarness.Password, Ct)).ShouldBe("s3cret");
    }

    [Fact]
    public async Task SetAsync_Refusals_HaveTheirCodesAndStoreNothing()
    {
        var tenant = new SettingsHarness(SettingsHarness.Acme);
        var platform = new SettingsHarness();
        var staff = new SettingsHarness(SettingsHarness.Acme, ActorType.Platform);

        (await Code(tenant, "unknown.key", SettingScope.Tenant, "1")).ShouldBe(EventCodes.Configuration.SettingNotFound);
        (await Code(tenant, "test.days", SettingScope.User, "1")).ShouldBe(EventCodes.Configuration.SettingScopeNotAllowed);
        (await Code(tenant, "test.days", SettingScope.PlatformAndTenant, "1")).ShouldBe(EventCodes.Configuration.SettingScopeNotAllowed);
        (await Code(tenant, "test.platformOnly", SettingScope.Tenant, "1")).ShouldBe(EventCodes.Configuration.SettingScopeNotAllowed);
        (await Code(tenant, "test.days", SettingScope.Tenant, "0")).ShouldBe(EventCodes.Configuration.SettingValueInvalid);
        (await Code(tenant, "test.days", SettingScope.Tenant, "\"30\"")).ShouldBe(EventCodes.Configuration.SettingValueInvalid);
        (await Code(platform, "test.days", SettingScope.Tenant, "30")).ShouldBe(EventCodes.Tenancy.TenantRequired);
        (await Code(staff, "test.theme", SettingScope.User, "\"dark\"")).ShouldBe(EventCodes.Configuration.SettingScopeNotAllowed);

        tenant.Stores.Tenant.ShouldBeEmpty();
        tenant.Cache.Invalidated.ShouldBeEmpty();
    }

    [Fact]
    public async Task ResetAsync_RemovesTheValueAtEachLevel()
    {
        var harness = new SettingsHarness(SettingsHarness.Acme, ActorType.User);
        harness.Stores.Platform["test.theme"] = "\"platform\"";
        harness.Stores.Tenant["test.theme"] = "\"tenant\"";
        harness.Stores.User[(SettingsHarness.UserId, "test.theme")] = "\"dark\"";

        (await harness.Manager.ResetAsync(new ResetSetting("test.theme", SettingScope.User), Ct)).IsSuccess.ShouldBeTrue();
        (await harness.Manager.ResetAsync(new ResetSetting("test.theme", SettingScope.Tenant), Ct)).IsSuccess.ShouldBeTrue();
        (await harness.Manager.ResetAsync(new ResetSetting("test.theme", SettingScope.Platform), Ct)).IsSuccess.ShouldBeTrue();

        harness.Stores.User.ShouldBeEmpty();
        harness.Stores.Tenant.ShouldBeEmpty();
        harness.Stores.Platform.ShouldBeEmpty();
        harness.Cache.Invalidated.ShouldBe(["t:acme:configuration", "platform:configuration"]);
        (await harness.Provider.GetAsync(SettingsHarness.Theme, Ct)).ShouldBe("light");
        (await harness.Manager.ResetAsync(new ResetSetting("unknown.key", SettingScope.Tenant), Ct)).Error!.Code.ShouldBe(EventCodes.Configuration.SettingNotFound);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static async Task<int> Code(SettingsHarness harness, string key, SettingScope level, string json) =>
        (await harness.Manager.SetAsync(new SetSetting(key, level, Json(json)), Ct)).Error!.Code;
}
