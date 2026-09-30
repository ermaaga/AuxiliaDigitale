using Auxilia.Application.Abstractions.Localization;
using Auxilia.Application.Localization;
using Auxilia.Diagnostics;
using Auxilia.Domain.Localization;
using Auxilia.Domain.Platform;
using Auxilia.Application.Abstractions.Tenancy;

using NSubstitute;

namespace Auxilia.Application.Tests.Localization;

public sealed class LocalizationServicesTests
{
    private readonly LocalizationHarness harness = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Bundle_FallsBackToTenantDefaultThenEnglishThenTheKey()
    {
        var source = new BundleSource(
            ["Save", "Cancel", "Only.en", "Orphan"],
            [
                new("Save", "de", "Speichern"), new("Save", "it", "Salva"), new("Save", "en", "Save"),
                new("Cancel", "it", "Annulla"), new("Cancel", "en", "Cancel"),
                new("Only.en", "en", "English only"),
            ]);

        var bundle = TranslationBundles.Build("de", "it", source);

        bundle.Language.ShouldBe("de");
        bundle.Values.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (pair.Key, pair.Value)).ShouldBe(
            [("Cancel", "Annulla"), ("Only.en", "English only"), ("Orphan", "Orphan"), ("Save", "Speichern")]);
        TranslationBundles.FallbackChain("en", "en").ShouldBe(["en"]);
        TranslationBundles.FallbackChain("de", null).ShouldBe(["de", "en"]);
    }

    [Fact]
    public void BundleETag_DependsOnTheContentOnly()
    {
        var source = new BundleSource(["Save"], [new("Save", "it", "Salva")]);
        var changed = new BundleSource(["Save"], [new("Save", "it", "Memorizza")]);

        var first = TranslationBundles.Build("it", "it", source).ETag;

        first.ShouldMatch("^\"[0-9a-f]{32}\"$");
        TranslationBundles.Build("it", "it", source).ETag.ShouldBe(first);
        TranslationBundles.Build("it", "it", changed).ETag.ShouldNotBe(first);
        TranslationBundles.Build("en", "it", source).ETag.ShouldNotBe(first);
    }

    [Fact]
    public async Task GetBundle_ServesActiveLanguagesFromTheCache()
    {
        harness.Data.AddKey("Save", en: "Save", it: "Salva");

        (await harness.Queries.GetBundleAsync("it", Ct)).Value.Values["Save"].ShouldBe("Salva");
        (await harness.Queries.GetBundleAsync("it", Ct)).Value.Values["Save"].ShouldBe("Salva");
        harness.Data.BundleLoads.ShouldBe(1);
        harness.Cache.Requests.ShouldContain(entry => entry.Key == "t:acme:localization:bundle:it" && entry.Tags.Contains("t:acme:localization"));

        (await harness.Queries.GetBundleAsync("de", Ct)).Error!.Code.ShouldBe(EventCodes.Localization.LanguageNotFound);
        harness.Data.Languages.Single(language => language.Code == "en").SetActive(false);
        await harness.Languages.InvalidateTenantAsync("acme", Ct);
        (await harness.Queries.GetBundleAsync("en", Ct)).Error!.Code.ShouldBe(EventCodes.Localization.LanguageNotFound);
    }

    [Fact]
    public async Task ListLanguages_ActiveOnlyWithTheTenantDefaultFirst()
    {
        harness.Data.Languages.Add(new Language(Guid.CreateVersion7(), "de", "Deutsch"));
        harness.Data.Languages.Single(language => language.Code == "de").SetActive(false);

        var languages = await harness.Queries.ListLanguagesAsync(Ct);

        languages.Select(language => (language.Code, language.IsDefault)).ShouldBe([("it", true), ("en", false)]);
    }

    [Fact]
    public async Task WithoutTenant_TheSnapshotsAreEmpty()
    {
        var harness = new LocalizationHarness();
        harness.TenantContext.Current.Returns((TenantInfo?)null);
        harness.Data.AddKey("Save", en: "Save");

        (await harness.Languages.GetAsync(Ct)).Languages.ShouldBeEmpty();
        (await harness.Bundles.GetAsync("en", Ct)).Values.ShouldBeEmpty();
    }

    [Fact]
    public async Task LanguageStatsAndCategories_CountKeysAndMissingTranslations()
    {
        harness.Data.AddKey("Save", en: "Save", it: "Salva");
        harness.Data.AddKey("nav.cases", "nav", en: "Cases");

        var stats = await harness.Queries.ListLanguageStatsAsync(Ct);
        var categories = await harness.Queries.ListCategoriesAsync(Ct);

        stats.Select(language => (language.Code, language.IsDefault, language.TranslatedCount, language.MissingCount))
            .ShouldBe([("en", false, 2, 0), ("it", true, 1, 1)]);
        categories.Select(category => (category.Category, category.KeyCount)).ShouldBe([("common", 1), ("nav", 1)]);
    }

    [Fact]
    public async Task ListKeys_ValidatesPagingAndReportsMissingLanguages()
    {
        harness.Data.AddKey("Save", en: "Save", it: "Salva");
        harness.Data.AddKey("nav.cases", "nav", en: "Cases");

        var page = (await harness.Queries.ListKeysAsync(new ResourceKeyQuery(null, null, "it", null, 1, 25), Ct)).Value;

        page.TotalCount.ShouldBe(1);
        var key = page.Items.ShouldHaveSingleItem();
        (key.Key, key.Category, key.IsSystem).ShouldBe(("nav.cases", "nav", true));
        key.MissingLanguages.ShouldBe(["it"]);
        key.Translations.ShouldHaveSingleItem().ShouldBe(new Contracts.Localization.TranslationResponse("en", "Cases", false));

        var invalid = await harness.Queries.ListKeysAsync(new ResourceKeyQuery(new string('s', 201), null, null, "value", 0, 101), Ct);
        invalid.Error!.ValidationErrors.Keys.Order().ShouldBe(["page", "pageSize", "search", "sort"]);
        (await harness.Queries.ListKeysAsync(new ResourceKeyQuery("sal", null, null, "-category", 1, 10), Ct)).Value.Items.Single().Key.ShouldBe("Save");
    }

    [Fact]
    public async Task GetKey_ReturnsTheKeyOrNotFound()
    {
        var save = harness.Data.AddKey("Save", en: "Save", it: "Salva");

        (await harness.Queries.GetKeyAsync(save.Id, Ct)).Value.MissingLanguages.ShouldBeEmpty();
        (await harness.Queries.GetKeyAsync(Guid.CreateVersion7(), Ct)).Error!.Code.ShouldBe(EventCodes.Localization.ResourceKeyNotFound);
    }

    [Fact]
    public async Task Create_AddsACustomisedKeyAndEvictsTheBundles()
    {
        _ = await harness.Queries.GetBundleAsync("it", Ct);

        var created = await harness.Keys.CreateAsync(
            new CreateResourceKey(" app.clients.list.emptyTitle ", "app", "Empty list", new Dictionary<string, string> { ["en"] = "No clients", ["it"] = "Nessun cliente" }), Ct);

        var key = harness.Data.Keys.ShouldHaveSingleItem();
        (key.Id, key.Key, key.IsSystem).ShouldBe((created.Value, "app.clients.list.emptyTitle", false));
        key.Translations.ShouldAllBe(translation => translation.IsCustomized);
        harness.Cache.Invalidated.ShouldBe(["t:acme:localization"]);
        (await harness.Queries.GetBundleAsync("it", Ct)).Value.Values["app.clients.list.emptyTitle"].ShouldBe("Nessun cliente");
    }

    [Fact]
    public async Task Create_RefusesDuplicatesUnknownLanguagesAndInvalidValues()
    {
        harness.Data.AddKey("Save", en: "Save");

        (await harness.Keys.CreateAsync(new CreateResourceKey("Save", "common", null, null), Ct)).Error!.Code.ShouldBe(EventCodes.Localization.ResourceKeyExists);
        (await harness.Keys.CreateAsync(new CreateResourceKey("Other", "common", null, new Dictionary<string, string> { ["de"] = "x" }), Ct)).Error!.Code
            .ShouldBe(EventCodes.Localization.LanguageNotFound);
        (await harness.Keys.CreateAsync(new CreateResourceKey("Other", "common", null, new Dictionary<string, string> { ["en"] = " " }), Ct)).Error!.Code
            .ShouldBe(EventCodes.Localization.ResourceValueInvalid);
        (await harness.Keys.CreateAsync(new CreateResourceKey("bad key", "common", null, null), Ct)).Error!.Code.ShouldBe(EventCodes.Localization.ResourceValueInvalid);
        harness.Data.Keys.Count.ShouldBe(1);
        harness.Cache.Invalidated.ShouldBeEmpty();
    }

    [Fact]
    public async Task Update_ChangesCategoryAndDescription()
    {
        var save = harness.Data.AddKey("Save", en: "Save");

        (await harness.Keys.UpdateAsync(new UpdateResourceKey(save.Id, "app", "Toolbar button"), Ct)).IsSuccess.ShouldBeTrue();
        (save.Category, save.Description).ShouldBe(("app", "Toolbar button"));
        (await harness.Keys.UpdateAsync(new UpdateResourceKey(save.Id, "App!", null), Ct)).Error!.Code.ShouldBe(EventCodes.Localization.ResourceValueInvalid);
        (await harness.Keys.UpdateAsync(new UpdateResourceKey(Guid.CreateVersion7(), "app", null), Ct)).Error!.Code.ShouldBe(EventCodes.Localization.ResourceKeyNotFound);
        harness.Cache.Invalidated.ShouldBe(["t:acme:localization"]);
    }

    [Fact]
    public async Task SetAndRemoveTranslation_ChangeWhatClientsGet()
    {
        var save = harness.Data.AddKey("Save", en: "Save", it: "Salva");
        (await harness.Queries.GetBundleAsync("it", Ct)).Value.Values["Save"].ShouldBe("Salva");

        (await harness.Keys.SetTranslationAsync(save.Id, "it", "Memorizza", Ct)).IsSuccess.ShouldBeTrue();
        (await harness.Queries.GetBundleAsync("it", Ct)).Value.Values["Save"].ShouldBe("Memorizza");
        save.Translation("it")!.IsCustomized.ShouldBeTrue();

        (await harness.Keys.RemoveTranslationAsync(save.Id, "it", Ct)).IsSuccess.ShouldBeTrue();
        (await harness.Queries.GetBundleAsync("it", Ct)).Value.Values["Save"].ShouldBe("Save");

        (await harness.Keys.RemoveTranslationAsync(save.Id, "it", Ct)).Error!.Code.ShouldBe(EventCodes.Localization.TranslationNotFound);
        (await harness.Keys.SetTranslationAsync(save.Id, "de", "Speichern", Ct)).Error!.Code.ShouldBe(EventCodes.Localization.LanguageNotFound);
        (await harness.Keys.SetTranslationAsync(save.Id, "en", "", Ct)).Error!.Code.ShouldBe(EventCodes.Localization.ResourceValueInvalid);
        (await harness.Keys.SetTranslationAsync(Guid.CreateVersion7(), "en", "x", Ct)).Error!.Code.ShouldBe(EventCodes.Localization.ResourceKeyNotFound);
        harness.Cache.Invalidated.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Delete_RemovesTheKey()
    {
        var save = harness.Data.AddKey("Save", en: "Save");

        (await harness.Keys.DeleteAsync(save.Id, Ct)).IsSuccess.ShouldBeTrue();
        harness.Data.Keys.ShouldBeEmpty();
        (await harness.Keys.DeleteAsync(save.Id, Ct)).Error!.Code.ShouldBe(EventCodes.Localization.ResourceKeyNotFound);
    }

    [Fact]
    public async Task Localizer_UsesTheRecipientLanguageWithFallbacksAndArguments()
    {
        harness.Data.AddKey("email.greeting", "email", en: "Hello {name}", it: "Ciao {name}");
        harness.Data.AddKey("email.footer", "email", en: "Thanks");
        harness.Data.AddKey("email.empty", "email");
        var name = new Dictionary<string, object?> { ["name"] = "Anna" };

        (await harness.Localizer.GetAsync("email.greeting", "en", name, Ct)).ShouldBe("Hello Anna");
        (await harness.Localizer.GetAsync("email.greeting", "de", name, Ct)).ShouldBe("Ciao Anna");
        (await harness.Localizer.GetAsync("email.greeting", null, null, Ct)).ShouldBe("Ciao {name}");
        (await harness.Localizer.GetAsync("email.footer", "it", null, Ct)).ShouldBe("Thanks");
        (await harness.Localizer.GetAsync("email.empty", "it", null, Ct)).ShouldBe("email.empty");
        (await harness.Localizer.GetAsync("email.unknown", "it", null, Ct)).ShouldBe("email.unknown");
    }

    [Fact]
    public async Task Localizer_WithoutTenant_FallsBackToEnglish()
    {
        var harness = new LocalizationHarness(new TenantInfo(Guid.CreateVersion7(), "solo", TenantStatus.Active, "fr", "Europe/Paris"));
        harness.Data.AddKey("Save", en: "Save", it: "Salva");

        (await harness.Localizer.GetAsync("Save", "fr", null, Ct)).ShouldBe("Save");
    }
}
