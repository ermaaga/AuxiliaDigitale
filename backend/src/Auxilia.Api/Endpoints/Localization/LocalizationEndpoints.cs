using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Api.Tenancy;
using Auxilia.Application.Abstractions.Localization;
using Auxilia.Application.Localization;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Localization;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Auxilia.Api.Endpoints.Localization;

/// <summary>
/// Translations (F24). <c>/i18n</c>: the bundles of the tenant, anonymous so the sign-in pages render in the right
/// language, revalidated with <c>ETag</c>/<c>If-None-Match</c>. <c>/localization</c>: the resource editor of the System
/// console (D-18), only with a platform token scoped to the tenant (D-21); edits are visible at the next request.
/// </summary>
internal sealed class LocalizationEndpoints : IApiEndpoints
{
    public void Map(RouteGroupBuilder api)
    {
        var i18n = api.MapGroup("/i18n").WithTags("Localization").RequireTenant();

        i18n.MapGet("/languages", ListLanguagesAsync)
            .AllowAnonymous()
            .WithName("ListLanguages")
            .WithSummary("Active languages of the tenant, default first")
            .Produces<IReadOnlyList<LanguageResponse>>();

        i18n.MapGet("/{language}", GetBundleAsync)
            .AllowAnonymous()
            .WithName("GetTranslationBundle")
            .WithSummary("Translations of a language (key → text; fallback: tenant default language, English, the key) with ETag")
            .Produces<IReadOnlyDictionary<string, string>>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var editor = api.MapGroup("/localization").WithTags("Localization").RequirePlatformTenant();

        editor.MapGet("/languages", ListLanguageStatsAsync)
            .WithName("ListLanguageStats")
            .WithSummary("Every language with its translated and missing keys")
            .Produces<IReadOnlyList<LanguageStatsResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        editor.MapGet("/categories", ListCategoriesAsync)
            .WithName("ListResourceCategories")
            .WithSummary("Categories of the translation keys and their number of keys")
            .Produces<IReadOnlyList<ResourceCategoryResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        editor.MapGet("/keys", ListKeysAsync)
            .WithName("ListResourceKeys")
            .WithSummary("Translation keys, searched by key or text, filtered by category or missing language")
            .Produces<PagedResponse<ResourceKeyResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        editor.MapGet("/keys/{id:guid}", GetKeyAsync)
            .WithName("GetResourceKey")
            .WithSummary("A translation key with its translations")
            .Produces<ResourceKeyResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        editor.MapPost("/keys", CreateKeyAsync)
            .WithName("CreateResourceKey")
            .WithSummary("Adds a translation key, optionally with its first translations")
            .Produces<CreateResourceKeyResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        editor.MapPut("/keys/{id:guid}", UpdateKeyAsync)
            .WithName("UpdateResourceKey")
            .WithSummary("Changes category and description of a translation key")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        editor.MapDelete("/keys/{id:guid}", DeleteKeyAsync)
            .WithName("DeleteResourceKey")
            .WithSummary("Deletes a translation key and its translations")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        editor.MapPut("/keys/{id:guid}/translations/{language}", SetTranslationAsync)
            .WithName("SetTranslation")
            .WithSummary("Sets the translation of a language (marked as customised for the tenant)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        editor.MapDelete("/keys/{id:guid}/translations/{language}", RemoveTranslationAsync)
            .WithName("RemoveTranslation")
            .WithSummary("Removes the translation of a language (clients get the fallback)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListLanguagesAsync(ILocalizationQueryService localization, CancellationToken cancellationToken) =>
        TypedResults.Ok(await localization.ListLanguagesAsync(cancellationToken));

    private static async Task<IResult> GetBundleAsync(string language, HttpContext context, ILocalizationQueryService localization, CancellationToken cancellationToken) =>
        (await localization.GetBundleAsync(language, cancellationToken)).ToHttpResult(bundle =>
        {
            // Always revalidated: an edit must be visible at the next request (F24).
            var headers = context.Response.Headers;
            headers.ETag = bundle.ETag;
            headers.CacheControl = "no-cache";
            headers.Vary = "X-Tenant";

            var match = context.Request.Headers.IfNoneMatch.ToString();
            return EntityTagHeaderValue.TryParseList(match.Split(','), out var tags)
                && tags.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || string.Equals(tag.Tag.ToString(), bundle.ETag, StringComparison.Ordinal))
                ? TypedResults.StatusCode(StatusCodes.Status304NotModified)
                : TypedResults.Ok(bundle.Values);
        });

    private static async Task<IResult> ListLanguageStatsAsync(ILocalizationQueryService localization, CancellationToken cancellationToken) =>
        TypedResults.Ok(await localization.ListLanguageStatsAsync(cancellationToken));

    private static async Task<IResult> ListCategoriesAsync(ILocalizationQueryService localization, CancellationToken cancellationToken) =>
        TypedResults.Ok(await localization.ListCategoriesAsync(cancellationToken));

    private static async Task<IResult> ListKeysAsync(
        ILocalizationQueryService localization,
        string? search,
        [FromQuery(Name = "filter[category]")] string? category,
        [FromQuery(Name = "filter[missingLanguage]")] string? missingLanguage,
        string? sort,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken) =>
        (await localization.ListKeysAsync(new ResourceKeyQuery(search, category, missingLanguage, sort, page ?? 1, pageSize ?? 25), cancellationToken))
            .ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> GetKeyAsync(Guid id, ILocalizationQueryService localization, CancellationToken cancellationToken) =>
        (await localization.GetKeyAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> CreateKeyAsync(CreateResourceKeyRequest request, IResourceKeyManager keys, CancellationToken cancellationToken) =>
        (await keys.CreateAsync(new CreateResourceKey(request.Key, request.Category, request.Description, request.Translations), cancellationToken))
            .ToHttpResult(id => TypedResults.Created($"/api/v1/localization/keys/{id}", new CreateResourceKeyResponse(id)));

    private static async Task<IResult> UpdateKeyAsync(Guid id, UpdateResourceKeyRequest request, IResourceKeyManager keys, CancellationToken cancellationToken) =>
        (await keys.UpdateAsync(new UpdateResourceKey(id, request.Category, request.Description), cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> DeleteKeyAsync(Guid id, IResourceKeyManager keys, CancellationToken cancellationToken) =>
        (await keys.DeleteAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> SetTranslationAsync(Guid id, string language, SetTranslationRequest request, IResourceKeyManager keys, CancellationToken cancellationToken) =>
        (await keys.SetTranslationAsync(id, language, request.Value, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> RemoveTranslationAsync(Guid id, string language, IResourceKeyManager keys, CancellationToken cancellationToken) =>
        (await keys.RemoveTranslationAsync(id, language, cancellationToken)).ToHttpResult(TypedResults.NoContent);
}
