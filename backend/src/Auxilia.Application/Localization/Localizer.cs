using System.Globalization;

using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Localization.Public;
using Auxilia.Diagnostics;
using Auxilia.Domain.Localization;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Localization;

/// <inheritdoc cref="ILocalizer"/>
internal sealed class Localizer : ILocalizer
{
    private readonly LanguagesCache languages;
    private readonly TranslationBundleCache bundles;
    private readonly ITenantContext tenantContext;
    private readonly ILogger<Localizer> logger;

    public Localizer(LanguagesCache languages, TranslationBundleCache bundles, ITenantContext tenantContext, ILogger<Localizer> logger)
    {
        this.languages = languages;
        this.bundles = bundles;
        this.tenantContext = tenantContext;
        this.logger = logger;
    }

    public async Task<string> GetAsync(string key, string? languageCode, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var snapshot = await languages.GetAsync(cancellationToken);
        var language = snapshot.FindActive(languageCode)?.Code ?? tenantContext.Current?.DefaultLanguage ?? Language.English;
        var bundle = await bundles.GetAsync(language, cancellationToken);

        if (!bundle.Values.TryGetValue(key, out var text) || string.Equals(text, key, StringComparison.Ordinal))
        {
            Log.Localization.MissingKey(logger, key, language);
            return key;
        }

        return Format(text, arguments);
    }

    private static string Format(string text, IReadOnlyDictionary<string, object?>? arguments)
    {
        if (arguments is null)
        {
            return text;
        }

        foreach (var (name, value) in arguments)
        {
            text = text.Replace("{" + name + "}", Convert.ToString(value, CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        return text;
    }
}
