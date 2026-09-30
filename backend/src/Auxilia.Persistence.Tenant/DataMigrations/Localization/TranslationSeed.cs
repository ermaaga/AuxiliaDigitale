using System.Text.Json;

using Auxilia.Domain.Localization;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>A shipped translation key with its EN and IT values (skill auxilia-localization: both are mandatory).</summary>
public sealed record SeedTranslation(string Key, string Category, string En, string It);

/// <summary>
/// Shipped translations and their idempotent upsert (skill auxilia-data-migration). Each localization data-migration
/// has its own immutable file <c>DataMigrations/Localization/&lt;key&gt;.json</c> (embedded resource).
/// </summary>
public static class TranslationSeed
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The languages every tenant has; others are added by later data-migrations (F24).</summary>
    public static readonly IReadOnlyList<(string Code, string Name)> Languages = [(Language.English, "English"), ("it", "Italiano")];

    /// <summary>The content of a data-migration file, e.g. <c>Load("D_20260930_003")</c>.</summary>
    public static IReadOnlyList<SeedTranslation> Load(string migrationKey)
    {
        var name = $"Localization/{migrationKey}.json";
        using var stream = typeof(TranslationSeed).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource {name} not found.");
        return JsonSerializer.Deserialize<SeedTranslation[]>(stream, Json) ?? [];
    }

    /// <summary>Every shipped file, in key order (tests, fallback bundle generation).</summary>
    public static IReadOnlyList<SeedTranslation> LoadAll() =>
        typeof(TranslationSeed).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("Localization/", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .SelectMany(name => Load(name["Localization/".Length..^".json".Length]))
            .ToArray();

    /// <summary>Adds the missing languages (active).</summary>
    public static async Task EnsureLanguagesAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var existing = await db.Set<Language>().Select(language => language.Code).ToListAsync(cancellationToken);
        foreach (var (code, name) in Languages.Where(language => !existing.Contains(language.Code)))
        {
            db.Set<Language>().Add(new Language(Guid.CreateVersion7(), code, name));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Inserts missing keys (as system keys) and missing EN/IT translations; upgrades translations the tenant has not
    /// customised. Keys the tenant created or re-categorised keep their category and description.
    /// </summary>
    public static async Task UpsertAsync(TenantDbContext db, IReadOnlyList<SeedTranslation> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(entries);

        var names = entries.Select(entry => entry.Key).ToArray();
        var existing = await db.Set<ResourceKey>().Include(key => key.Translations)
            .Where(key => names.Contains(key.Key))
            .ToDictionaryAsync(key => key.Key, StringComparer.Ordinal, cancellationToken);

        foreach (var entry in entries)
        {
            if (!existing.TryGetValue(entry.Key, out var key))
            {
                key = ResourceKey.Create(Guid.CreateVersion7(), entry.Key, entry.Category, null, isSystem: true).Value;
                db.Set<ResourceKey>().Add(key);
                existing[entry.Key] = key;
            }

            key.UpgradeSystemTranslation(Language.English, entry.En);
            key.UpgradeSystemTranslation("it", entry.It);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
