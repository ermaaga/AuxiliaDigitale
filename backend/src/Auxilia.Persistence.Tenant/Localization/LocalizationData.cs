using Auxilia.Application.Abstractions.Localization;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Localization;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Localization;

internal sealed class LocalizationDataFactory(ITenantDbContextFactory databases) : ILocalizationDataFactory
{
    public async Task<ILocalizationData> OpenAsync(CancellationToken cancellationToken) =>
        new LocalizationData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="ILocalizationData"/>
internal sealed class LocalizationData(ITenantDbContext db) : ILocalizationData
{
    public async Task<IReadOnlyList<Language>> LanguagesAsync(CancellationToken cancellationToken) =>
        await db.Set<Language>().AsNoTracking().OrderBy(language => language.Code).ToListAsync(cancellationToken);

    public Task<ResourceKey?> FindKeyAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<ResourceKey>().Include(key => key.Translations).SingleOrDefaultAsync(key => key.Id == id, cancellationToken);

    public Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken) =>
        db.Set<ResourceKey>().AnyAsync(item => item.Key == key, cancellationToken);

    public void Add(ResourceKey key) => db.Set<ResourceKey>().Add(key);

    public void Remove(ResourceKey key) => db.Set<ResourceKey>().Remove(key);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();
}

/// <inheritdoc cref="ILocalizationReader"/>
internal sealed class LocalizationReader(ITenantDbContextFactory databases) : ILocalizationReader
{
    public async Task<IReadOnlyList<Language>> LanguagesAsync(CancellationToken cancellationToken)
    {
        await using var db = await databases.CreateAsync(cancellationToken);
        return await db.Set<Language>().AsNoTracking().OrderBy(language => language.Code).ToListAsync(cancellationToken);
    }

    public async Task<BundleSource> BundleSourceAsync(IReadOnlyCollection<string> languageCodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(languageCodes);

        await using var db = await databases.CreateAsync(cancellationToken);
        var keys = await db.Set<ResourceKey>().AsNoTracking().Select(key => key.Key).ToListAsync(cancellationToken);
        var translations = await db.Set<ResourceKey>().AsNoTracking()
            .SelectMany(key => key.Translations
                .Where(translation => languageCodes.Contains(translation.LanguageCode))
                .Select(translation => new BundleTranslation(key.Key, translation.LanguageCode, translation.Value)))
            .ToListAsync(cancellationToken);
        return new BundleSource(keys, translations);
    }

    public async Task<(IReadOnlyList<ResourceKey> Items, long TotalCount)> ListKeysAsync(ResourceKeyQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var db = await databases.CreateAsync(cancellationToken);
        var keys = db.Set<ResourceKey>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = "%" + query.Search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            keys = keys.Where(key => EF.Functions.ILike(key.Key, pattern, "\\")
                || key.Translations.Any(translation => EF.Functions.ILike(translation.Value, pattern, "\\")));
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            keys = keys.Where(key => key.Category == query.Category);
        }

        if (!string.IsNullOrWhiteSpace(query.MissingLanguage))
        {
            keys = keys.Where(key => !key.Translations.Any(translation => translation.LanguageCode == query.MissingLanguage));
        }

        var total = await keys.LongCountAsync(cancellationToken);
        var sorted = query.Sort switch
        {
            "-key" => keys.OrderByDescending(key => key.Key),
            "category" => keys.OrderBy(key => key.Category).ThenBy(key => key.Key),
            "-category" => keys.OrderByDescending(key => key.Category).ThenBy(key => key.Key),
            _ => keys.OrderBy(key => key.Key),
        };

        var items = await sorted
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Include(key => key.Translations)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<ResourceKey?> GetKeyAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await databases.CreateAsync(cancellationToken);
        return await db.Set<ResourceKey>().AsNoTracking().Include(key => key.Translations).SingleOrDefaultAsync(key => key.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, int>> CategoriesAsync(CancellationToken cancellationToken)
    {
        await using var db = await databases.CreateAsync(cancellationToken);
        return await db.Set<ResourceKey>().AsNoTracking()
            .GroupBy(key => key.Category)
            .Select(group => new { Category = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Category, group => group.Count, StringComparer.Ordinal, cancellationToken);
    }

    public async Task<(int KeyCount, IReadOnlyDictionary<string, int> TranslatedByLanguage)> CoverageAsync(CancellationToken cancellationToken)
    {
        await using var db = await databases.CreateAsync(cancellationToken);
        var keyCount = await db.Set<ResourceKey>().CountAsync(cancellationToken);
        var translated = await db.Set<ResourceTranslation>().AsNoTracking()
            .GroupBy(translation => translation.LanguageCode)
            .Select(group => new { Language = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Language, group => group.Count, StringComparer.Ordinal, cancellationToken);
        return (keyCount, translated);
    }
}
