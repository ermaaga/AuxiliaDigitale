using System.Text.Json;

using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Configuration;

namespace Auxilia.Application.Tests.Configuration;

public sealed class SettingDefinitionTests
{
    private static readonly SettingDefinition<int> Days = new("cases.expiry.expiringDays", "Cases", 7, isValid: days => days is >= 1 and <= 365);

    [Theory]
    [InlineData("cases")]
    [InlineData("Cases.expiry")]
    [InlineData("cases..expiry")]
    [InlineData("cases.expiry-days")]
    [InlineData("cases.expiry.")]
    public void Constructor_KeyNotDottedCamelCase_Throws(string key) =>
        Should.Throw<ArgumentException>(() => new SettingDefinition<bool>(key, "Cases", false));

    [Fact]
    public void Constructor_NoLevelOrInvalidDefault_Throws()
    {
        Should.Throw<ArgumentException>(() => new SettingDefinition<bool>("cases.enabled", "Cases", false, SettingScope.None));
        Should.Throw<ArgumentException>(() => new SettingDefinition<int>("cases.days", "Cases", 0, isValid: days => days > 0));
        Should.Throw<ArgumentException>(() => new SettingDefinition<string>("cases." + new string('a', 150), "Cases", "x"));
    }

    [Fact]
    public void Definition_ExposesMetadata()
    {
        Days.DefaultJson.ShouldBe("7");
        Days.ValueType.ShouldBe(typeof(int));
        Days.IsSecret.ShouldBeFalse();
        Days.DescriptionKey.ShouldBe("settings.cases.expiry.expiringDays.description");
        Days.Allows(SettingScope.Tenant).ShouldBeTrue();
        Days.Allows(SettingScope.User).ShouldBeFalse();
        Days.Allows(SettingScope.None).ShouldBeFalse();
    }

    [Theory]
    [InlineData("30", true)]
    [InlineData("0", false)]
    [InlineData("\"30\"", false)]
    [InlineData("true", false)]
    [InlineData("null", false)]
    [InlineData("{", false)]
    public void TryRead_ChecksTypeAndRule(string json, bool expected) =>
        Days.TryRead(json, out _).ShouldBe(expected);

    [Fact]
    public void TryNormalize_ValidValue_ReturnsCanonicalJson()
    {
        var language = new SettingDefinition<string>("registration.defaultLanguage", "Directory", "it", isValid: SettingRules.IsLanguageCode);

        language.TryNormalize(JsonDocument.Parse("\"en\"").RootElement, out var json).ShouldBeTrue();
        json.ShouldBe("\"en\"");
        language.TryNormalize(JsonDocument.Parse("\"english\"").RootElement, out _).ShouldBeFalse();
        Days.TryNormalize(JsonDocument.Parse("12").RootElement, out var days).ShouldBeTrue();
        days.ShouldBe("12");
    }

    [Fact]
    public void Enums_AreStoredAsStrings()
    {
        var level = new SettingDefinition<SettingSource>("logging.source", "Configuration", SettingSource.Tenant);

        level.DefaultJson.ShouldBe("\"Tenant\"");
        level.TryRead("\"Platform\"", out var value).ShouldBeTrue();
        value.ShouldBe(SettingSource.Platform);
    }

    [Fact]
    public void SecretDefinition_AcceptsNonEmptyStringsAndIsNeverAUserSetting()
    {
        var secret = new SecretSettingDefinition("documents.ftp.password", "Documents");

        secret.IsSecret.ShouldBeTrue();
        secret.DefaultJson.ShouldBeNull();
        secret.ValueType.ShouldBe(typeof(string));
        secret.TryNormalize(JsonDocument.Parse("\"s3cret\"").RootElement, out var json).ShouldBeTrue();
        json.ShouldBe("\"s3cret\"");
        secret.TryNormalize(JsonDocument.Parse("\"\"").RootElement, out _).ShouldBeFalse();
        secret.TryNormalize(JsonDocument.Parse("42").RootElement, out _).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => new SecretSettingDefinition("documents.ftp.password", "Documents", SettingScope.Tenant | SettingScope.User));
    }

    [Fact]
    public void Registry_FindsByKeyAndRejectsTwoDefinitionsWithTheSameKey()
    {
        var registry = new SettingDefinitionRegistry([Days, Days]);

        registry.Find(Days.Key).ShouldBeSameAs(Days);
        registry.Find("unknown.key").ShouldBeNull();
        registry.All.ShouldHaveSingleItem();
        Should.Throw<InvalidOperationException>(() => new SettingDefinitionRegistry([Days, new SettingDefinition<int>(Days.Key, "Cases", 1)]));
    }

    [Fact]
    public void LanguageRule_AcceptsTwoLowerCaseLetters()
    {
        SettingRules.IsLanguageCode("it").ShouldBeTrue();
        SettingRules.IsLanguageCode("IT").ShouldBeFalse();
        SettingRules.IsLanguageCode(null!).ShouldBeFalse();
    }
}
