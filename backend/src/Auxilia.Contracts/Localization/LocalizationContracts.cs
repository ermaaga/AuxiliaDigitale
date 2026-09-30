namespace Auxilia.Contracts.Localization;

/// <summary><c>GET /i18n/languages</c>: an active language of the tenant; <c>isDefault</c> marks the tenant default.</summary>
public sealed record LanguageResponse(string Code, string Name, bool IsDefault);

/// <summary>
/// <c>GET /localization/languages</c> (System console): every language with how many keys it translates and how many
/// it misses.
/// </summary>
public sealed record LanguageStatsResponse(string Code, string Name, bool IsActive, bool IsDefault, int TranslatedCount, int MissingCount);

/// <summary><c>GET /localization/categories</c>: a category of keys and its number of keys.</summary>
public sealed record ResourceCategoryResponse(string Category, int KeyCount);

/// <summary>A translation of a key; <c>isCustomized</c> = edited for the tenant (data-migrations leave it alone).</summary>
public sealed record TranslationResponse(string LanguageCode, string Value, bool IsCustomized);

/// <summary>
/// A translation key with its translations; <c>missingLanguages</c> lists the active languages without a translation
/// (clients get the fallback: tenant default language, then English, then the key).
/// </summary>
public sealed record ResourceKeyResponse(
    Guid Id,
    string Key,
    string Category,
    string? Description,
    bool IsSystem,
    IReadOnlyList<TranslationResponse> Translations,
    IReadOnlyList<string> MissingLanguages);

/// <summary><c>POST /localization/keys</c>: a new key and, optionally, its first translations (language code → value).</summary>
public sealed record CreateResourceKeyRequest(string Key, string Category, string? Description, IReadOnlyDictionary<string, string>? Translations);

/// <summary><c>POST /localization/keys</c>: the id of the new key.</summary>
public sealed record CreateResourceKeyResponse(Guid Id);

/// <summary><c>PUT /localization/keys/{id}</c>: category and description (the key name never changes).</summary>
public sealed record UpdateResourceKeyRequest(string Category, string? Description);

/// <summary><c>PUT /localization/keys/{id}/translations/{language}</c>: the new value (marked as customised).</summary>
public sealed record SetTranslationRequest(string Value);
