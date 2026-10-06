using Auxilia.Domain.Localization;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.MigrationRunner.LegacyImport.Steps;

/// <summary>
/// E-05 (F24): <c>Languages</c>, <c>ResourceKeys</c>, <c>ResourceTranslations</c> → <c>localization.*</c>. The seed
/// already holds the legacy dictionary with its key names (D_20260930_003): a legacy value different from the tenant's is
/// set as a customised translation; a key only the legacy has becomes a tenant key (not system). Keys of the workout
/// plans (D-09) are left out.
/// </summary>
internal sealed class LocalizationStep : ILegacyImportStep
{
    public const string LanguagesTable = "Languages";
    public const string TranslationsTable = "ResourceTranslations";
    public const string KeysTable = "ResourceKeys";

    public string Name => "translations";

    /// <summary>Keys of the removed workout plans (D-09).</summary>
    public static bool IsWorkoutKey(string key) =>
        key.Contains("Workout", StringComparison.OrdinalIgnoreCase) || key.Contains("Exercise", StringComparison.OrdinalIgnoreCase);

    /// <summary>A category the new rules accept (lower-case first letter, letters and digits); otherwise <c>legacy</c>.</summary>
    public static string Category(string legacy)
    {
        var category = string.IsNullOrWhiteSpace(legacy) ? "legacy" : char.ToLowerInvariant(legacy.Trim()[0]) + legacy.Trim()[1..];
        return ResourceKey.IsValidCategory(category) ? category : "legacy";
    }

    public async Task RunAsync(LegacyImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var db = context.Tenant;
        var languages = await db.Set<Language>().ToDictionaryAsync(language => language.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var legacyLanguages = await context.Legacy.Languages.ToListAsync(cancellationToken);
        var languageResult = context.Report.For(LanguagesTable);
        foreach (var legacy in legacyLanguages)
        {
            var code = legacy.Code.Trim().ToLowerInvariant();
            if (!Language.IsValidCode(code))
            {
                context.Report.Skip(LanguagesTable, legacy.Id, "language code not valid");
                continue;
            }

            if (languages.TryGetValue(code, out var language))
            {
                if (language.IsActive != legacy.IsActive && legacy.IsActive)
                {
                    language.SetActive(true);
                    languageResult.Updated++;
                }

                continue;
            }

            var created = new Language(context.Ids.NewId(), code, LegacyText.Fit(legacy.Name, Language.NameMaxLength, out _));
            created.SetActive(legacy.IsActive);
            db.Add(created);
            languages[code] = created;
            languageResult.Created++;
        }

        var codes = legacyLanguages.ToDictionary(language => language.Id, language => language.Code.Trim().ToLowerInvariant());
        var keys = await db.Set<ResourceKey>().ToDictionaryAsync(key => key.Key, StringComparer.Ordinal, cancellationToken);
        var legacyKeys = await context.Legacy.ResourceKeys.ToDictionaryAsync(key => key.Id, cancellationToken);
        var result = context.Report.For(TranslationsTable);
        var keyResult = context.Report.For(KeysTable);
        foreach (var legacy in await context.Legacy.ResourceTranslations.OrderBy(row => row.Id).ToListAsync(cancellationToken))
        {
            if (!legacyKeys.TryGetValue(legacy.ResourceKeyId, out var legacyKey) || !codes.TryGetValue(legacy.LanguageId, out var code) || !languages.ContainsKey(code))
            {
                context.Report.Skip(TranslationsTable, legacy.Id, "key or language not migrated");
                continue;
            }

            if (IsWorkoutKey(legacyKey.Key))
            {
                result.Excluded++;
                continue;
            }

            if (!ResourceTranslation.IsValidValue(legacy.Value))
            {
                context.Report.Skip(TranslationsTable, legacy.Id, "empty or too long value");
                continue;
            }

            if (!keys.TryGetValue(legacyKey.Key, out var key))
            {
                var created = ResourceKey.Create(context.Ids.NewId(), legacyKey.Key, Category(legacyKey.Category), description: null, isSystem: false);
                if (created.IsFailure)
                {
                    context.Report.Skip(TranslationsTable, legacy.Id, "key name not valid");
                    continue;
                }

                key = created.Value;
                db.Add(key);
                keys[key.Key] = key;
                keyResult.Created++;
            }

            if (key.Translation(code)?.Value == legacy.Value)
            {
                continue;
            }

            key.SetTranslation(code, legacy.Value);
            result.Updated++;
        }
    }
}
