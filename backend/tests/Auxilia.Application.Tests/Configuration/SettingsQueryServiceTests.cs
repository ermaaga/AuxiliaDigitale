using System.Text.Json;

using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Configuration;
using Auxilia.Diagnostics;

namespace Auxilia.Application.Tests.Configuration;

public sealed class SettingsQueryServiceTests
{
    private enum Mode
    {
        Off,
        On,
    }

    private static readonly SettingDefinition<Mode> ModeSetting = new("test.mode", "Alpha", Mode.Off);

    private static readonly SettingDefinition<string> Size = new("test.size", "Test", "m", choices: ["s", "m", "l"]);

    private static readonly SettingDefinition<decimal> Rate = new("test.rate", "Test", 1.5m);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SettingsQueryService Service(SettingsHarness harness) =>
        new(new SettingDefinitionRegistry([SettingsHarness.Enabled, SettingsHarness.Days, SettingsHarness.Theme, SettingsHarness.PlatformOnly, SettingsHarness.Password, ModeSetting, Size, Rate]),
            harness.Snapshots, harness.TenantContext);

    [Fact]
    public async Task List_TenantSettingsOnly_OrderedByModuleThenKey()
    {
        var harness = new SettingsHarness(SettingsHarness.Acme);

        var settings = (await Service(harness).ListTenantSettingsAsync(Ct)).Value;

        settings.Select(setting => setting.Key).ShouldBe(["test.mode", "test.days", "test.enabled", "test.password", "test.rate", "test.size", "test.theme"]);
    }

    [Fact]
    public async Task List_ShowsEveryLevelAndTheEffectiveSource()
    {
        var harness = new SettingsHarness(SettingsHarness.Acme);
        harness.Stores.Platform["test.days"] = "14";
        harness.Stores.Platform["test.enabled"] = "true";
        harness.Stores.Tenant["test.enabled"] = "false";

        var settings = (await Service(harness).ListTenantSettingsAsync(Ct)).Value.ToDictionary(setting => setting.Key);

        var days = settings["test.days"];
        days.Kind.ShouldBe("integer");
        days.DefaultValue!.Value.GetInt32().ShouldBe(7);
        days.PlatformValue!.Value.GetInt32().ShouldBe(14);
        days.TenantValue.ShouldBeNull();
        days.EffectiveValue!.Value.GetInt32().ShouldBe(14);
        days.Source.ShouldBe("Platform");
        days.HasTenantValue.ShouldBeFalse();

        var enabled = settings["test.enabled"];
        enabled.Kind.ShouldBe("boolean");
        enabled.EffectiveValue!.Value.GetBoolean().ShouldBeFalse();
        enabled.Source.ShouldBe("Tenant");
        enabled.HasTenantValue.ShouldBeTrue();

        settings["test.theme"].Source.ShouldBe("Default");
        settings["test.theme"].Kind.ShouldBe("string");
        settings["test.rate"].Kind.ShouldBe("number");
    }

    [Fact]
    public async Task List_Choices_ComeFromEnumsAndFixedLists()
    {
        var harness = new SettingsHarness(SettingsHarness.Acme);

        var settings = (await Service(harness).ListTenantSettingsAsync(Ct)).Value.ToDictionary(setting => setting.Key);

        settings["test.mode"].Kind.ShouldBe("choice");
        settings["test.mode"].Choices.ShouldBe(["Off", "On"]);
        settings["test.mode"].EffectiveValue!.Value.GetString().ShouldBe("Off");
        settings["test.size"].Kind.ShouldBe("choice");
        settings["test.size"].Choices.ShouldBe(["s", "m", "l"]);
    }

    [Fact]
    public async Task List_SecretValuesNeverLeave()
    {
        var harness = new SettingsHarness(SettingsHarness.Acme);
        harness.Stores.Tenant["test.password"] = JsonSerializer.Serialize("enc:s3cret");

        var secret = (await Service(harness).ListTenantSettingsAsync(Ct)).Value.Single(setting => setting.Key == "test.password");

        secret.Kind.ShouldBe("secret");
        secret.DefaultValue.ShouldBeNull();
        secret.TenantValue.ShouldBeNull();
        secret.EffectiveValue.ShouldBeNull();
        secret.Source.ShouldBe("Tenant");
        secret.HasTenantValue.ShouldBeTrue();
    }

    [Fact]
    public async Task List_InvalidStoredValue_IsIgnored()
    {
        var harness = new SettingsHarness(SettingsHarness.Acme);
        harness.Stores.Tenant["test.days"] = "0";

        var days = (await Service(harness).GetTenantSettingAsync("test.days", Ct)).Value;

        days.TenantValue.ShouldBeNull();
        days.HasTenantValue.ShouldBeTrue();
        days.Source.ShouldBe("Default");
    }

    [Fact]
    public async Task Get_UnknownOrPlatformOnlyKey_IsNotFound()
    {
        var service = Service(new SettingsHarness(SettingsHarness.Acme));

        (await service.GetTenantSettingAsync("test.nope", Ct)).Error!.Code.ShouldBe(EventCodes.Configuration.SettingNotFound);
        (await service.GetTenantSettingAsync("test.platformOnly", Ct)).Error!.Code.ShouldBe(EventCodes.Configuration.SettingNotFound);
    }

    [Fact]
    public async Task WithoutTenant_IsRefused()
    {
        var service = Service(new SettingsHarness());

        (await service.ListTenantSettingsAsync(Ct)).Error!.Code.ShouldBe(EventCodes.Tenancy.TenantRequired);
        (await service.GetTenantSettingAsync("test.days", Ct)).Error!.Code.ShouldBe(EventCodes.Tenancy.TenantRequired);
    }

    [Fact]
    public void ChoicesAndHexColour_AreValidated()
    {
        Size.TryNormalize(JsonDocument.Parse("\"xl\"").RootElement, out _).ShouldBeFalse();
        Size.TryNormalize(JsonDocument.Parse("\"l\"").RootElement, out _).ShouldBeTrue();
        Should.Throw<ArgumentException>(() => new SettingDefinition<string>("test.bad", "Test", "x", choices: ["a"]));
        ModeSetting.TryNormalize(JsonDocument.Parse("\"On\"").RootElement, out _).ShouldBeTrue();

        SettingRules.IsHexColor("#667eea").ShouldBeTrue();
        SettingRules.IsHexColor("#FFF").ShouldBeTrue();
        SettingRules.IsHexColor("667eea").ShouldBeFalse();
        SettingRules.IsHexColor("#66e").ShouldBeTrue();
        SettingRules.IsHexColor("#6677").ShouldBeFalse();
        SettingRules.IsHexColor("red").ShouldBeFalse();
        SettingRules.IsHexColor("#667eea;background:url(x)").ShouldBeFalse();
    }
}
