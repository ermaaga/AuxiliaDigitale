using Auxilia.Application.Abstractions.Caching;
using Auxilia.Application.Abstractions.Configuration;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Contracts.Configuration;
using Auxilia.Diagnostics;
using Auxilia.Domain.Configuration;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Configuration;

/// <summary>Branding images of the current tenant (F23), uploaded by the System from the console.</summary>
public interface IBrandingManager
{
    /// <summary>Stores (or replaces) the image; PNG, JPEG or WebP within the size limit of the kind.</summary>
    Task<Result> SetAssetAsync(BrandingAssetKind kind, byte[] content, CancellationToken cancellationToken);

    /// <summary>Removes the image (nothing stored is not an error).</summary>
    Task<Result> RemoveAssetAsync(BrandingAssetKind kind, CancellationToken cancellationToken);
}

/// <summary>The public branding of the current tenant (login pages, app shell) and its images.</summary>
public interface IBrandingQueryService
{
    /// <summary>Settings and image versions, cached per tenant (<c>t:{slug}:configuration:branding</c>).</summary>
    Task<BrandingResponse> GetAsync(CancellationToken cancellationToken);

    Task<Result<BrandingImage>> GetAssetAsync(BrandingAssetKind kind, CancellationToken cancellationToken);
}

public sealed record BrandingImage(string ContentType, byte[] Content, string Version);

internal sealed class BrandingManager(IOperationRunner operations, IBrandingAssetStore store, ITenantContext tenantContext, IReferenceDataCache cache)
    : IBrandingManager
{
    public Task<Result> SetAssetAsync(BrandingAssetKind kind, byte[] content, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Configuration.SetBrandingAsset, new { Asset = kind.ToString(), Bytes = content?.Length }, async scope =>
        {
            ArgumentNullException.ThrowIfNull(content);
            if (!tenantContext.IsResolved)
            {
                return Errors.Tenancy.TenantRequired();
            }

            var existing = await store.FindAsync(kind, cancellationToken);
            if (existing is null)
            {
                var created = BrandingAsset.Create(Guid.CreateVersion7(), kind, content);
                if (created.IsFailure)
                {
                    return Result.Failure(created.Error!);
                }

                store.Add(created.Value);
            }
            else
            {
                var replaced = existing.Replace(content);
                if (replaced.IsFailure)
                {
                    return replaced;
                }
            }

            await store.SaveChangesAsync(cancellationToken);
            scope.OnCommitted(InvalidateAsync);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> RemoveAssetAsync(BrandingAssetKind kind, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Configuration.RemoveBrandingAsset, new { Asset = kind.ToString() }, async scope =>
        {
            if (!tenantContext.IsResolved)
            {
                return Errors.Tenancy.TenantRequired();
            }

            if (await store.FindAsync(kind, cancellationToken) is { } existing)
            {
                store.Remove(existing);
                await store.SaveChangesAsync(cancellationToken);
                scope.OnCommitted(InvalidateAsync);
            }

            return Result.Success();
        }, cancellationToken);

    /// <summary>The cached branding carries the image versions: every node drops it after the commit.</summary>
    private Task InvalidateAsync(CancellationToken cancellationToken) =>
        cache.InvalidateAsync(CacheTags.Tenant(tenantContext.Current!.Slug, SettingsSnapshotCache.ModuleName), cancellationToken);
}

/// <summary>
/// Branding of the current tenant, cached with the settings snapshot's tag (<c>t:{slug}:configuration</c>): a setting
/// change or a new image drops both at once.
/// </summary>
internal sealed class BrandingCache : ReferenceDataCache<BrandingResponse>
{
    private readonly ISettingsProvider settings;
    private readonly IBrandingAssetStore assets;

    public BrandingCache(IReferenceDataCache cache, ITenantContext tenantContext, ISettingsProvider settings, IBrandingAssetStore assets)
        : base(cache, tenantContext)
    {
        this.settings = settings;
        this.assets = assets;
    }

    protected override string Module => SettingsSnapshotCache.ModuleName;

    protected override string Entity => "branding";

    protected override async Task<BrandingResponse> LoadAsync(TenantInfo? tenant, string variant, CancellationToken cancellationToken)
    {
        var versions = tenant is null
            ? new Dictionary<BrandingAssetKind, string>()
            : await assets.VersionsAsync(cancellationToken);

        return new BrandingResponse(
            await settings.GetAsync(BrandingSettings.AppName, cancellationToken),
            await settings.GetAsync(BrandingSettings.UseAppName, cancellationToken),
            (await settings.GetAsync(BrandingSettings.ThemeFill, cancellationToken)).ToString(),
            await settings.GetAsync(BrandingSettings.PrimaryColor, cancellationToken),
            await settings.GetAsync(BrandingSettings.AccentColor, cancellationToken),
            new BrandingBackgroundResponse(
                (await settings.GetAsync(BrandingSettings.BackgroundType, cancellationToken)).ToString(),
                await settings.GetAsync(BrandingSettings.BackgroundStartColor, cancellationToken),
                await settings.GetAsync(BrandingSettings.BackgroundEndColor, cancellationToken),
                await settings.GetAsync(BrandingSettings.BackgroundColor, cancellationToken),
                Version(versions, BrandingAssetKind.Background)),
            Version(versions, BrandingAssetKind.Logo));
    }

    /// <summary>The first 16 hex digits of the hash: short in URLs, still unique enough to bust caches.</summary>
    private static string? Version(IReadOnlyDictionary<BrandingAssetKind, string> versions, BrandingAssetKind kind) =>
        versions.TryGetValue(kind, out var hash) ? hash[..16] : null;
}

internal sealed class BrandingQueryService(BrandingCache cache, IBrandingAssetStore assets, ITenantContext tenantContext) : IBrandingQueryService
{
    public Task<BrandingResponse> GetAsync(CancellationToken cancellationToken) => cache.GetAsync(cancellationToken);

    public async Task<Result<BrandingImage>> GetAssetAsync(BrandingAssetKind kind, CancellationToken cancellationToken)
    {
        if (!tenantContext.IsResolved || await assets.ReadAsync(kind, cancellationToken) is not { } asset)
        {
            return Errors.Configuration.BrandingAssetNotFound();
        }

        return new BrandingImage(asset.ContentType, asset.Content, asset.Hash[..16]);
    }
}
