using Auxilia.Application.Abstractions.Localization;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Localization;
using Auxilia.Diagnostics;
using Auxilia.Domain.Localization;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Localization;

/// <summary>
/// Translations of the current tenant (F24): the cached bundles clients load (<c>GET /i18n/{lang}</c>, anonymous so the
/// login page can render) and the lists of the resource editor of the System console.
/// </summary>
public interface ILocalizationQueryService
{
    /// <summary>Active languages, tenant default first.</summary>
    Task<IReadOnlyList<LanguageResponse>> ListLanguagesAsync(CancellationToken cancellationToken);

    /// <summary>The bundle of an active language (404 <c>AUX-21003</c> otherwise), from the cache.</summary>
    Task<Result<TranslationBundle>> GetBundleAsync(string language, CancellationToken cancellationToken);

    Task<IReadOnlyList<LanguageStatsResponse>> ListLanguageStatsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ResourceCategoryResponse>> ListCategoriesAsync(CancellationToken cancellationToken);

    /// <summary>Paged, sorted by key by default; <c>pageSize</c> at most 100.</summary>
    Task<Result<PagedResponse<ResourceKeyResponse>>> ListKeysAsync(ResourceKeyQuery query, CancellationToken cancellationToken);

    Task<Result<ResourceKeyResponse>> GetKeyAsync(Guid id, CancellationToken cancellationToken);
}

internal sealed class LocalizationQueryService : ILocalizationQueryService
{
    public const int MaxPageSize = 100;

    private static readonly string[] Sorts = ["key", "-key", "category", "-category"];

    private readonly LanguagesCache languages;
    private readonly TranslationBundleCache bundles;
    private readonly ILocalizationReader reader;
    private readonly ITenantContext tenantContext;

    public LocalizationQueryService(LanguagesCache languages, TranslationBundleCache bundles, ILocalizationReader reader, ITenantContext tenantContext)
    {
        this.languages = languages;
        this.bundles = bundles;
        this.reader = reader;
        this.tenantContext = tenantContext;
    }

    private string? TenantDefault => tenantContext.Current?.DefaultLanguage;

    public async Task<IReadOnlyList<LanguageResponse>> ListLanguagesAsync(CancellationToken cancellationToken)
    {
        var snapshot = await languages.GetAsync(cancellationToken);
        return snapshot.Languages
            .Where(language => language.IsActive)
            .Select(language => new LanguageResponse(language.Code, language.Name, IsDefault(language.Code)))
            .OrderByDescending(language => language.IsDefault)
            .ThenBy(language => language.Code, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<Result<TranslationBundle>> GetBundleAsync(string language, CancellationToken cancellationToken)
    {
        var snapshot = await languages.GetAsync(cancellationToken);
        if (snapshot.FindActive(language) is null)
        {
            return Errors.Localization.LanguageNotFound(language);
        }

        return await bundles.GetAsync(language, cancellationToken);
    }

    public async Task<IReadOnlyList<LanguageStatsResponse>> ListLanguageStatsAsync(CancellationToken cancellationToken)
    {
        var all = await reader.LanguagesAsync(cancellationToken);
        var (keyCount, translated) = await reader.CoverageAsync(cancellationToken);
        return all
            .Select(language =>
            {
                var count = translated.GetValueOrDefault(language.Code);
                return new LanguageStatsResponse(language.Code, language.Name, language.IsActive, IsDefault(language.Code), count, keyCount - count);
            })
            .OrderBy(language => language.Code, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IReadOnlyList<ResourceCategoryResponse>> ListCategoriesAsync(CancellationToken cancellationToken) =>
        (await reader.CategoriesAsync(cancellationToken))
            .OrderBy(category => category.Key, StringComparer.Ordinal)
            .Select(category => new ResourceCategoryResponse(category.Key, category.Value))
            .ToArray();

    public async Task<Result<PagedResponse<ResourceKeyResponse>>> ListKeysAsync(ResourceKeyQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (query.Page < 1)
        {
            errors["page"] = ["validation.paging.page"];
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = ["validation.paging.pageSize"];
        }

        if (query.Sort is { } sort && !Sorts.Contains(sort, StringComparer.Ordinal))
        {
            errors["sort"] = ["validation.paging.sort"];
        }

        if (query.Search is { Length: > Abstractions.Paging.PageRequest.MaxSearchLength })
        {
            errors["search"] = ["validation.maximumLength"];
        }

        if (errors.Count > 0)
        {
            return Errors.Host.ValidationFailed(errors);
        }

        var active = await ActiveCodesAsync(cancellationToken);
        var (items, total) = await reader.ListKeysAsync(query, cancellationToken);
        return new PagedResponse<ResourceKeyResponse>(items.Select(key => ToResponse(key, active)).ToArray(), query.Page, query.PageSize, total);
    }

    public async Task<Result<ResourceKeyResponse>> GetKeyAsync(Guid id, CancellationToken cancellationToken)
    {
        var key = await reader.GetKeyAsync(id, cancellationToken);
        return key is null
            ? Errors.Localization.ResourceKeyNotFound()
            : ToResponse(key, await ActiveCodesAsync(cancellationToken));
    }

    private static ResourceKeyResponse ToResponse(ResourceKey key, IReadOnlyList<string> activeLanguages) =>
        new(
            key.Id,
            key.Key,
            key.Category,
            key.Description,
            key.IsSystem,
            key.Translations
                .OrderBy(translation => translation.LanguageCode, StringComparer.Ordinal)
                .Select(translation => new TranslationResponse(translation.LanguageCode, translation.Value, translation.IsCustomized))
                .ToArray(),
            activeLanguages.Where(code => key.Translation(code) is null).ToArray());

    private async Task<IReadOnlyList<string>> ActiveCodesAsync(CancellationToken cancellationToken) =>
        (await languages.GetAsync(cancellationToken)).Languages
            .Where(language => language.IsActive)
            .Select(language => language.Code)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

    private bool IsDefault(string code) => string.Equals(code, TenantDefault, StringComparison.Ordinal);
}
