using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;

using Auxilia.Application.Abstractions.Caching;
using Auxilia.Application.Abstractions.Localization;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Domain.Localization;

namespace Auxilia.Application.Localization;

/// <summary>The languages of a tenant, cached as <c>t:{slug}:localization:languages:current</c>.</summary>
[ImmutableObject(true)]
public sealed record LanguageSnapshot(IReadOnlyList<LanguageInfo> Languages)
{
    public LanguageInfo? FindActive(string? code) =>
        Languages.FirstOrDefault(language => language.IsActive && string.Equals(language.Code, code, StringComparison.Ordinal));
}

public sealed record LanguageInfo(string Code, string Name, bool IsActive);

/// <summary>
/// The flat bundle of one language (key → text) with the fallbacks applied, and its <see cref="ETag"/> (a hash of the
/// content: the same on every node, different after any change). Cached as <c>t:{slug}:localization:bundle:{lang}</c>.
/// </summary>
[ImmutableObject(true)]
public sealed record TranslationBundle(string Language, string ETag, IReadOnlyDictionary<string, string> Values);

internal sealed class LanguagesCache : ReferenceDataCache<LanguageSnapshot>
{
    private readonly ILocalizationReader reader;

    public LanguagesCache(IReferenceDataCache cache, ITenantContext tenantContext, ILocalizationReader reader)
        : base(cache, tenantContext) => this.reader = reader;

    protected override string Module => LocalizationModule.ModuleCode;

    protected override string Entity => "languages";

    protected override async Task<LanguageSnapshot> LoadAsync(TenantInfo? tenant, string variant, CancellationToken cancellationToken)
    {
        if (tenant is null)
        {
            return new LanguageSnapshot([]);
        }

        var languages = await reader.LanguagesAsync(cancellationToken);
        return new LanguageSnapshot(languages.Select(language => new LanguageInfo(language.Code, language.Name, language.IsActive)).ToArray());
    }
}

/// <summary>One bundle per language (the variant); evicted with the whole <c>t:{slug}:localization</c> tag on every edit.</summary>
internal sealed class TranslationBundleCache : ReferenceDataCache<TranslationBundle>
{
    private readonly ILocalizationReader reader;

    public TranslationBundleCache(IReferenceDataCache cache, ITenantContext tenantContext, ILocalizationReader reader)
        : base(cache, tenantContext) => this.reader = reader;

    protected override string Module => LocalizationModule.ModuleCode;

    protected override string Entity => "bundle";

    protected override async Task<TranslationBundle> LoadAsync(TenantInfo? tenant, string variant, CancellationToken cancellationToken)
    {
        if (tenant is null)
        {
            return TranslationBundles.Build(variant, null, new BundleSource([], []));
        }

        var chain = TranslationBundles.FallbackChain(variant, tenant.DefaultLanguage);
        var source = await reader.BundleSourceAsync(chain, cancellationToken);
        return TranslationBundles.Build(variant, tenant.DefaultLanguage, source);
    }
}

/// <summary>F24 fallback: requested language → tenant default language → English → the key itself.</summary>
internal static class TranslationBundles
{
    public static IReadOnlyList<string> FallbackChain(string language, string? tenantDefault) =>
        new[] { language, tenantDefault, Language.English }
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public static TranslationBundle Build(string language, string? tenantDefault, BundleSource source)
    {
        var chain = FallbackChain(language, tenantDefault);
        var byKey = source.Translations
            .GroupBy(translation => translation.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToDictionary(item => item.LanguageCode, item => item.Value, StringComparer.Ordinal), StringComparer.Ordinal);

        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in source.Keys)
        {
            var value = key;
            if (byKey.TryGetValue(key, out var translations))
            {
                value = chain.Select(code => translations.GetValueOrDefault(code)).FirstOrDefault(text => text is not null) ?? key;
            }

            values[key] = value;
        }

        return new TranslationBundle(language, ETagOf(language, values), new Dictionary<string, string>(values, StringComparer.Ordinal));
    }

    /// <summary>Strong ETag: SHA-256 of the language and the sorted key/value pairs (first 128 bits, hex).</summary>
    private static string ETagOf(string language, SortedDictionary<string, string> values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(language + "\n"));
        foreach (var (key, value) in values)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(key + "\0" + value + "\n"));
        }

        return "\"" + Convert.ToHexStringLower(hash.GetHashAndReset().AsSpan(0, 16)) + "\"";
    }
}
