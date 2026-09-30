using Auxilia.Domain.Localization;

namespace Auxilia.Application.Abstractions.Localization;

/// <summary>Opens a unit of work on the translations of the current tenant (implemented in Persistence.Tenant).</summary>
public interface ILocalizationDataFactory
{
    Task<ILocalizationData> OpenAsync(CancellationToken cancellationToken);
}

/// <summary>Languages and translation keys of the current tenant, tracked for changes (writes of the Manager).</summary>
public interface ILocalizationData : IAsyncDisposable
{
    Task<IReadOnlyList<Language>> LanguagesAsync(CancellationToken cancellationToken);

    /// <summary>The key with its translations.</summary>
    Task<ResourceKey?> FindKeyAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken);

    void Add(ResourceKey key);

    void Remove(ResourceKey key);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Filters of the key list of the resource editor (F24).</summary>
/// <param name="Search">Part of the key or of any translation (case-insensitive).</param>
/// <param name="MissingLanguage">Only keys without a translation in this language.</param>
/// <param name="Sort"><c>key</c>, <c>-key</c>, <c>category</c>, <c>-category</c> (validated by the query service).</param>
public sealed record ResourceKeyQuery(string? Search, string? Category, string? MissingLanguage, string? Sort, int Page, int PageSize);

/// <summary>Everything a translation bundle is built from: every key and its translations in the requested languages.</summary>
public sealed record BundleSource(IReadOnlyList<string> Keys, IReadOnlyList<BundleTranslation> Translations);

public sealed record BundleTranslation(string Key, string LanguageCode, string Value);

/// <summary>Read side of the translations (no tracking, projections, paging in the database).</summary>
public interface ILocalizationReader
{
    Task<IReadOnlyList<Language>> LanguagesAsync(CancellationToken cancellationToken);

    Task<BundleSource> BundleSourceAsync(IReadOnlyCollection<string> languageCodes, CancellationToken cancellationToken);

    /// <summary>Keys (with translations) matching the query, sorted and paged.</summary>
    Task<(IReadOnlyList<ResourceKey> Items, long TotalCount)> ListKeysAsync(ResourceKeyQuery query, CancellationToken cancellationToken);

    Task<ResourceKey?> GetKeyAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Category → number of keys.</summary>
    Task<IReadOnlyDictionary<string, int>> CategoriesAsync(CancellationToken cancellationToken);

    /// <summary>Number of keys and, per language, the number of translations.</summary>
    Task<(int KeyCount, IReadOnlyDictionary<string, int> TranslatedByLanguage)> CoverageAsync(CancellationToken cancellationToken);
}
