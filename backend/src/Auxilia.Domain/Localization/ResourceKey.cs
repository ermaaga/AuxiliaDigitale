using System.Text.RegularExpressions;

using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Localization;

/// <summary>
/// A translation key of the tenant (<c>localization.resource_keys</c>, F24) with its translations, one per language.
/// Shipped keys (<see cref="IsSystem"/>) are seeded by data-migrations; a translation edited by the System is marked
/// customised and no data-migration overwrites it again. Legacy keys keep their original names (<c>Save</c>,
/// <c>ExportPDF</c>); new keys follow <c>&lt;area&gt;.&lt;module&gt;.&lt;element&gt;</c> (skill auxilia-localization).
/// </summary>
public sealed partial class ResourceKey : AggregateRoot<Guid>, IAuditable
{
    public const int KeyMaxLength = 200;
    public const int CategoryMaxLength = 50;
    public const int DescriptionMaxLength = 500;

    private readonly List<ResourceTranslation> translations = [];

    private ResourceKey(Guid id, string key, string category, string? description, bool isSystem)
        : base(id)
    {
        Key = key;
        Category = category;
        Description = description;
        IsSystem = isSystem;
    }

    private ResourceKey()
    {
        Key = Category = string.Empty;
    }

    public string Key { get; private set; }

    /// <summary><c>common</c>, <c>nav</c>, <c>app</c>, <c>validation</c>, <c>errors</c>, <c>email</c>, <c>pdf</c>, <c>notification</c>…</summary>
    public string Category { get; private set; }

    /// <summary>Where the text appears, for translators.</summary>
    public string? Description { get; private set; }

    /// <summary>Shipped with the product (seeded); a tenant may still customise its translations.</summary>
    public bool IsSystem { get; private set; }

    public IReadOnlyCollection<ResourceTranslation> Translations => translations;

    /// <summary>Letters, digits, <c>.</c>, <c>_</c>, <c>-</c>; starts with a letter (<c>errors.AUX-12005</c> is valid).</summary>
    public static bool IsValidKey(string? key) => key is { Length: <= KeyMaxLength } && KeyPattern().IsMatch(key);

    /// <summary>Lower camel case: <c>common</c>, <c>notification</c>.</summary>
    public static bool IsValidCategory(string? category) => category is { Length: <= CategoryMaxLength } && CategoryPattern().IsMatch(category);

    public static Result<ResourceKey> Create(Guid id, string key, string category, string? description, bool isSystem)
    {
        if (!IsValidKey(key))
        {
            return Errors.Localization.ResourceValueInvalid("key");
        }

        var invalid = Validate(category, description);
        return invalid is null ? new ResourceKey(id, key, category, Normalize(description), isSystem) : invalid;
    }

    public Result Update(string category, string? description)
    {
        if (Validate(category, description) is { } invalid)
        {
            return invalid;
        }

        Category = category;
        Description = Normalize(description);
        return Result.Success();
    }

    public ResourceTranslation? Translation(string languageCode) =>
        translations.SingleOrDefault(translation => translation.LanguageCode == languageCode);

    /// <summary>An edit of the tenant (System console): creates or replaces the translation and marks it customised.</summary>
    public Result SetTranslation(string languageCode, string value)
    {
        if (!Language.IsValidCode(languageCode))
        {
            return Errors.Localization.ResourceValueInvalid("language");
        }

        if (!ResourceTranslation.IsValidValue(value))
        {
            return Errors.Localization.ResourceValueInvalid("value");
        }

        if (Translation(languageCode) is { } existing)
        {
            existing.Customize(value);
        }
        else
        {
            translations.Add(new ResourceTranslation(Guid.CreateVersion7(), Id, languageCode, value, isCustomized: true));
        }

        return Result.Success();
    }

    /// <summary>
    /// A shipped value (data-migration): adds a missing translation or upgrades one the tenant has not customised.
    /// Returns whether anything changed.
    /// </summary>
    public bool UpgradeSystemTranslation(string languageCode, string value)
    {
        if (!Language.IsValidCode(languageCode) || !ResourceTranslation.IsValidValue(value))
        {
            throw new ArgumentException($"Invalid shipped translation of {Key} ({languageCode}).", nameof(value));
        }

        if (Translation(languageCode) is not { } existing)
        {
            translations.Add(new ResourceTranslation(Guid.CreateVersion7(), Id, languageCode, value, isCustomized: false));
            return true;
        }

        return existing.UpgradeSystemValue(value);
    }

    /// <summary>Removes the translation of a language (clients then get the fallback). Returns whether it existed.</summary>
    public bool RemoveTranslation(string languageCode) =>
        Translation(languageCode) is { } existing && translations.Remove(existing);

    private static Error? Validate(string category, string? description)
    {
        if (!IsValidCategory(category))
        {
            return Errors.Localization.ResourceValueInvalid("category");
        }

        return description is { Length: > DescriptionMaxLength } ? Errors.Localization.ResourceValueInvalid("description") : null;
    }

    private static string? Normalize(string? description) => string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_.\-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^[a-z][A-Za-z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CategoryPattern();
}

/// <summary>The value of a <see cref="ResourceKey"/> in one language (<c>localization.resource_translations</c>).</summary>
public sealed class ResourceTranslation : Entity<Guid>, IAuditable
{
    public const int ValueMaxLength = 4000;

    internal ResourceTranslation(Guid id, Guid resourceKeyId, string languageCode, string value, bool isCustomized)
        : base(id)
    {
        ResourceKeyId = resourceKeyId;
        LanguageCode = languageCode;
        Value = value;
        IsCustomized = isCustomized;
    }

    private ResourceTranslation()
    {
        LanguageCode = Value = string.Empty;
    }

    public Guid ResourceKeyId { get; private set; }

    public string LanguageCode { get; private set; }

    /// <summary>ICU MessageFormat text (<c>{name}</c>, plurals); legacy values keep their <c>{0}</c> placeholders.</summary>
    public string Value { get; private set; }

    /// <summary>Edited by the tenant: data-migrations leave it alone.</summary>
    public bool IsCustomized { get; private set; }

    public static bool IsValidValue(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= ValueMaxLength;

    internal void Customize(string value)
    {
        Value = value;
        IsCustomized = true;
    }

    internal bool UpgradeSystemValue(string value)
    {
        if (IsCustomized || Value == value)
        {
            return false;
        }

        Value = value;
        return true;
    }
}
