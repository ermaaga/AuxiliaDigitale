using Auxilia.Application.Localization;

namespace Auxilia.Application.Localization.Public;

/// <summary>The languages of the current tenant for other modules (cached, F24).</summary>
public interface ITenantLanguages
{
    /// <summary>The language exists and is active for the tenant (exact code, e.g. <c>it</c>).</summary>
    Task<bool> IsActiveAsync(string? languageCode, CancellationToken cancellationToken);
}

internal sealed class TenantLanguages(LanguagesCache languages) : ITenantLanguages
{
    public async Task<bool> IsActiveAsync(string? languageCode, CancellationToken cancellationToken) =>
        !string.IsNullOrWhiteSpace(languageCode) && (await languages.GetAsync(cancellationToken)).FindActive(languageCode) is not null;
}
