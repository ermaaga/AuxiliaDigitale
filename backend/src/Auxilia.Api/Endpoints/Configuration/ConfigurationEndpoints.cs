using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Api.Tenancy;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Configuration;
using Auxilia.Contracts.Configuration;
using Auxilia.Diagnostics;
using Auxilia.Domain.Configuration;
using Auxilia.SharedKernel.Results;

using Microsoft.Net.Http.Headers;

namespace Auxilia.Api.Endpoints.Configuration;

/// <summary>
/// Tenant settings and branding (F23). <c>/settings</c>: the typed settings of the tenant, edited by the System with a
/// platform token scoped to the tenant (D-21). <c>/branding</c>: what the sign-in pages and the app shell need,
/// anonymous; its images are uploaded with the tenant-scoped token and served with their hash as ETag.
/// </summary>
internal sealed class ConfigurationEndpoints : IApiEndpoints
{
    /// <summary>Above the largest image limit: a bigger body is refused before it is read into memory.</summary>
    private const long UploadMaxBytes = BrandingAsset.BackgroundMaxBytes + (64 * 1024);

    public void Map(RouteGroupBuilder api)
    {
        var settings = api.MapGroup("/settings").WithTags("Configuration").RequirePlatformTenant();

        settings.MapGet("/", ListSettingsAsync)
            .WithName("ListTenantSettings")
            .WithSummary("Settings of the tenant with default, platform, tenant and effective value")
            .Produces<IReadOnlyList<SettingResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        settings.MapPut("/{key}", SetSettingAsync)
            .WithName("SetTenantSetting")
            .WithSummary("Sets the tenant-level value of a setting")
            .Produces<SettingResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        settings.MapDelete("/{key}", ResetSettingAsync)
            .WithName("ResetTenantSetting")
            .WithSummary("Removes the tenant-level value, so the platform value or the default applies again")
            .Produces<SettingResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var branding = api.MapGroup("/branding").WithTags("Configuration").RequireTenant();

        branding.MapGet("/", GetBrandingAsync)
            .AllowAnonymous()
            .WithName("GetBranding")
            .WithSummary("Branding of the tenant: app name, theme, login background and image versions")
            .Produces<BrandingResponse>();

        branding.MapGet("/{asset}", GetBrandingAssetAsync)
            .AllowAnonymous()
            .WithName("GetBrandingAsset")
            .WithSummary("A branding image (logo or background) with its hash as ETag")
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var images = api.MapGroup("/branding").WithTags("Configuration").RequirePlatformTenant();

        // A bearer token, not a cookie, authorises the upload: there is no form to forge (the BFF checks its own header).
        images.MapPut("/{asset}", SetBrandingAssetAsync)
            .DisableAntiforgery()
            .WithName("SetBrandingAsset")
            .WithSummary("Uploads the logo (≤ 512 KB) or the login background (≤ 2 MB): PNG, JPEG or WebP")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<BrandingResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        images.MapDelete("/{asset}", RemoveBrandingAssetAsync)
            .WithName("RemoveBrandingAsset")
            .WithSummary("Removes the logo or the login background image")
            .Produces<BrandingResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListSettingsAsync(ISettingsQueryService query, CancellationToken cancellationToken) =>
        (await query.ListTenantSettingsAsync(cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> SetSettingAsync(
        string key, SetSettingRequest request, ISettingsManager manager, ISettingsQueryService query, CancellationToken cancellationToken)
    {
        // Only the settings the tenant level allows exist here: the others are 404, not a validation error.
        var current = await query.GetTenantSettingAsync(key, cancellationToken);
        if (current.IsFailure)
        {
            return current.ToHttpResult(TypedResults.Ok);
        }

        var set = await manager.SetAsync(new SetSetting(key, SettingScope.Tenant, request.Value), cancellationToken);
        return set.IsFailure
            ? set.ToHttpResult(TypedResults.NoContent)
            : (await query.GetTenantSettingAsync(key, cancellationToken)).ToHttpResult(TypedResults.Ok);
    }

    private static async Task<IResult> ResetSettingAsync(string key, ISettingsManager manager, ISettingsQueryService query, CancellationToken cancellationToken)
    {
        var current = await query.GetTenantSettingAsync(key, cancellationToken);
        if (current.IsFailure)
        {
            return current.ToHttpResult(TypedResults.Ok);
        }

        var reset = await manager.ResetAsync(new ResetSetting(key, SettingScope.Tenant), cancellationToken);
        return reset.IsFailure
            ? reset.ToHttpResult(TypedResults.NoContent)
            : (await query.GetTenantSettingAsync(key, cancellationToken)).ToHttpResult(TypedResults.Ok);
    }

    private static async Task<IResult> GetBrandingAsync(HttpContext context, IBrandingQueryService branding, CancellationToken cancellationToken)
    {
        context.Response.Headers.Vary = "X-Tenant";
        context.Response.Headers.CacheControl = "no-cache";
        return TypedResults.Ok(await branding.GetAsync(cancellationToken));
    }

    private static async Task<IResult> GetBrandingAssetAsync(string asset, string? v, HttpContext context, IBrandingQueryService branding, CancellationToken cancellationToken)
    {
        if (!TryParse(asset, out var kind))
        {
            return Result.Failure(Errors.Configuration.BrandingAssetNotFound()).ToHttpResult(TypedResults.NoContent);
        }

        return (await branding.GetAssetAsync(kind, cancellationToken)).ToHttpResult(image =>
        {
            var etag = $"\"{image.Version}\"";
            var headers = context.Response.Headers;
            headers.ETag = etag;
            headers.Vary = "X-Tenant";
            // A URL that names the current version never changes; any other one is revalidated.
            headers.CacheControl = string.Equals(v, image.Version, StringComparison.Ordinal)
                ? "public, max-age=31536000, immutable"
                : "no-cache";

            var match = context.Request.Headers.IfNoneMatch.ToString();
            return EntityTagHeaderValue.TryParseList(match.Split(','), out var tags)
                && tags.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || string.Equals(tag.Tag.ToString(), etag, StringComparison.Ordinal))
                ? TypedResults.StatusCode(StatusCodes.Status304NotModified)
                : TypedResults.File(image.Content, image.ContentType);
        });
    }

    private static async Task<IResult> SetBrandingAssetAsync(
        string asset, IFormFile file, IBrandingManager manager, IBrandingQueryService branding, CancellationToken cancellationToken)
    {
        if (!TryParse(asset, out var kind))
        {
            return Result.Failure(Errors.Configuration.BrandingAssetNotFound()).ToHttpResult(TypedResults.NoContent);
        }

        if (file.Length > UploadMaxBytes)
        {
            return Result.Failure(Errors.Configuration.BrandingImageTooLarge(BrandingAsset.MaxBytes(kind) / 1024)).ToHttpResult(TypedResults.NoContent);
        }

        byte[] content;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        var set = await manager.SetAssetAsync(kind, content, cancellationToken);
        return set.IsFailure
            ? set.ToHttpResult(TypedResults.NoContent)
            : TypedResults.Ok(await branding.GetAsync(cancellationToken));
    }

    private static async Task<IResult> RemoveBrandingAssetAsync(string asset, IBrandingManager manager, IBrandingQueryService branding, CancellationToken cancellationToken)
    {
        if (!TryParse(asset, out var kind))
        {
            return Result.Failure(Errors.Configuration.BrandingAssetNotFound()).ToHttpResult(TypedResults.NoContent);
        }

        var removed = await manager.RemoveAssetAsync(kind, cancellationToken);
        return removed.IsFailure
            ? removed.ToHttpResult(TypedResults.NoContent)
            : TypedResults.Ok(await branding.GetAsync(cancellationToken));
    }

    /// <summary>Only the lower-case names (<c>logo</c>, <c>background</c>): numbers and other spellings are unknown.</summary>
    private static bool TryParse(string asset, out BrandingAssetKind kind)
    {
        kind = asset switch
        {
            "logo" => BrandingAssetKind.Logo,
            "background" => BrandingAssetKind.Background,
            _ => (BrandingAssetKind)(-1),
        };
        return Enum.IsDefined(kind);
    }
}
