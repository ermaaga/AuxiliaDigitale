using Auxilia.Application;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Domain.Localization;
using Auxilia.Persistence.Tenant.DataMigrations.Localization;

using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Persistence.Tests.Localization;

/// <summary>The shipped translations (F24, skill auxilia-localization): valid, EN + IT, and complete for the backend keys.</summary>
public sealed class TranslationSeedTests
{
    private static readonly IReadOnlyList<SeedTranslation> All = TranslationSeed.LoadAll();

    [Fact]
    public void EveryKeyIsValidUniqueAndHasEnglishAndItalian()
    {
        All.Select(entry => entry.Key).ShouldBeUnique();
        All.ShouldAllBe(entry => ResourceKey.IsValidKey(entry.Key) && ResourceKey.IsValidCategory(entry.Category));
        All.ShouldAllBe(entry => ResourceTranslation.IsValidValue(entry.En) && ResourceTranslation.IsValidValue(entry.It));
    }

    [Fact]
    public void LegacyDictionary_IsCompleteWithoutWorkoutKeys()
    {
        var legacy = TranslationSeed.Load("D_20260930_003");

        // 535 keys of the baseline (Security_Update) minus 22 used only by workout plans (D-09).
        legacy.Count.ShouldBe(513);
        legacy.Select(entry => entry.Key).ShouldContain("OtpLogin");
        legacy.Select(entry => entry.Key).ShouldContain("DownloadZipConfirm");
        legacy.Select(entry => entry.Key).ShouldNotContain("WorkoutPlans");
        legacy.Select(entry => entry.Key).ShouldNotContain("Exercise");
        legacy.Single(entry => entry.Key == "Save").ShouldBe(new SeedTranslation("Save", "common", "Save", "Salva"));
        legacy.Single(entry => entry.Key == "Group").It.ShouldBe("Gruppo");
    }

    [Fact]
    public void BackendKeys_ModulesNavigationAndPermissions_AreTranslated()
    {
        using var services = new ServiceCollection().AddLogging().AddApplication().BuildServiceProvider();
        var modules = services.GetRequiredService<IModuleRegistry>().All;
        var keys = All.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);

        var expected = modules.Select(module => module.NameKey)
            .Concat(modules.SelectMany(module => module.Navigation).Select(entry => entry.LabelKey))
            .Concat(modules.SelectMany(module => module.Permissions).Select(permission => permission.DescriptionKey))
            .Concat(TranslationSeed.Languages.Select(language => "languages." + language.Code))
            .Append("errors.generic");

        expected.Where(key => !keys.Contains(key)).ShouldBeEmpty();
    }
}
