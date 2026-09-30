using Auxilia.Diagnostics;
using Auxilia.Domain.Localization;

namespace Auxilia.Domain.Tests.Localization;

public sealed class LocalizationDomainTests
{
    [Theory]
    [InlineData("en", true)]
    [InlineData("it", true)]
    [InlineData("pt-BR", true)]
    [InlineData("EN", false)]
    [InlineData("english", false)]
    [InlineData("pt-br", false)]
    [InlineData(null, false)]
    public void LanguageCode_IsIsoLanguageWithOptionalRegion(string? code, bool valid) =>
        Language.IsValidCode(code).ShouldBe(valid);

    [Fact]
    public void Language_ValidatesAndCanBeDeactivated()
    {
        Should.Throw<ArgumentException>(() => new Language(Guid.CreateVersion7(), "Italian", "Italiano"));
        Should.Throw<ArgumentException>(() => new Language(Guid.CreateVersion7(), "it", " "));

        var language = new Language(Guid.CreateVersion7(), "it", "Italiano");
        (language.Code, language.Name, language.IsActive).ShouldBe(("it", "Italiano", true));
        language.SetActive(false);
        language.IsActive.ShouldBeFalse();
    }

    [Theory]
    [InlineData("Save", true)]
    [InlineData("app.cases.detail.advanceButton", true)]
    [InlineData("errors.AUX-12005", true)]
    [InlineData("validation.paging.page_size", true)]
    [InlineData("1Save", false)]
    [InlineData("has space", false)]
    [InlineData("", false)]
    public void Key_AllowsLegacyAndDottedNames(string key, bool valid) =>
        ResourceKey.IsValidKey(key).ShouldBe(valid);

    [Fact]
    public void Create_ValidatesKeyCategoryAndDescription()
    {
        Code(ResourceKey.Create(Guid.CreateVersion7(), "bad key", "common", null, false).Error!).ShouldBe(("key", EventCodes.Localization.ResourceValueInvalid));
        ResourceKey.Create(Guid.CreateVersion7(), new string('a', 201), "common", null, false).IsFailure.ShouldBeTrue();
        Code(ResourceKey.Create(Guid.CreateVersion7(), "Save", "Common", null, false).Error!).ShouldBe(("category", EventCodes.Localization.ResourceValueInvalid));
        Code(ResourceKey.Create(Guid.CreateVersion7(), "Save", "common", new string('d', 501), false).Error!).ShouldBe(("description", EventCodes.Localization.ResourceValueInvalid));

        var key = ResourceKey.Create(Guid.CreateVersion7(), "Save", "common", "  Save button  ", isSystem: true).Value;
        (key.Key, key.Category, key.Description, key.IsSystem).ShouldBe(("Save", "common", "Save button", true));
        key.Update("app", " ").IsSuccess.ShouldBeTrue();
        (key.Category, key.Description).ShouldBe(("app", (string?)null));
        key.Update("", null).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void SetTranslation_AddsOrReplacesAndMarksCustomised()
    {
        var key = ResourceKey.Create(Guid.CreateVersion7(), "Save", "common", null, true).Value;

        key.SetTranslation("it", "Salva").IsSuccess.ShouldBeTrue();
        key.SetTranslation("it", "Memorizza").IsSuccess.ShouldBeTrue();
        key.SetTranslation("xx-yy", "?").Error!.Code.ShouldBe(EventCodes.Localization.ResourceValueInvalid);
        key.SetTranslation("en", " ").Error!.Code.ShouldBe(EventCodes.Localization.ResourceValueInvalid);
        key.SetTranslation("en", new string('v', 4001)).IsFailure.ShouldBeTrue();

        var translation = key.Translations.ShouldHaveSingleItem();
        (translation.LanguageCode, translation.Value, translation.IsCustomized, translation.ResourceKeyId).ShouldBe(("it", "Memorizza", true, key.Id));
    }

    [Fact]
    public void UpgradeSystemTranslation_AddsMissingAndNeverOverwritesCustomised()
    {
        var key = ResourceKey.Create(Guid.CreateVersion7(), "Save", "common", null, true).Value;

        key.UpgradeSystemTranslation("en", "Save").ShouldBeTrue();
        key.UpgradeSystemTranslation("en", "Save").ShouldBeFalse();
        key.UpgradeSystemTranslation("en", "Save now").ShouldBeTrue();
        key.Translation("en")!.IsCustomized.ShouldBeFalse();

        key.SetTranslation("en", "Store");
        key.UpgradeSystemTranslation("en", "Save again").ShouldBeFalse();
        key.Translation("en")!.Value.ShouldBe("Store");

        Should.Throw<ArgumentException>(() => key.UpgradeSystemTranslation("en", ""));
    }

    [Fact]
    public void RemoveTranslation_ReportsWhetherItExisted()
    {
        var key = ResourceKey.Create(Guid.CreateVersion7(), "Save", "common", null, true).Value;
        key.UpgradeSystemTranslation("it", "Salva");

        key.RemoveTranslation("en").ShouldBeFalse();
        key.RemoveTranslation("it").ShouldBeTrue();
        key.Translations.ShouldBeEmpty();
    }

    private static (string Field, int Code) Code(Auxilia.SharedKernel.Results.Error error) => (error.ValidationErrors.Keys.Single(), error.Code);
}
