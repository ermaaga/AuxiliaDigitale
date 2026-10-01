using Auxilia.Application.Abstractions.Configuration;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Configuration;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Configuration;

/// <summary>
/// Branding images through <see cref="ITenantDbContextFactory"/>. Writes use one context per scope, so an image found and
/// changed is saved by the same unit of work (and joins the running operation's transaction); reads open their own
/// short-lived context, so they also work after that operation has ended.
/// </summary>
internal sealed class BrandingAssetStore(ITenantDbContextFactory databases) : IBrandingAssetStore, IAsyncDisposable
{
    private ITenantDbContext? db;

    public async Task<BrandingAsset?> FindAsync(BrandingAssetKind kind, CancellationToken cancellationToken) =>
        await (await DbAsync(cancellationToken)).Set<BrandingAsset>().SingleOrDefaultAsync(asset => asset.Kind == kind, cancellationToken);

    public async Task<BrandingAsset?> ReadAsync(BrandingAssetKind kind, CancellationToken cancellationToken)
    {
        await using var read = await databases.CreateAsync(cancellationToken);
        return await read.Set<BrandingAsset>().AsNoTracking().SingleOrDefaultAsync(asset => asset.Kind == kind, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<BrandingAssetKind, string>> VersionsAsync(CancellationToken cancellationToken)
    {
        // Projected: the versions never load the image bytes.
        await using var read = await databases.CreateAsync(cancellationToken);
        return await read.Set<BrandingAsset>()
            .Select(asset => new { asset.Kind, asset.Hash })
            .ToDictionaryAsync(asset => asset.Kind, asset => asset.Hash, cancellationToken);
    }

    public void Add(BrandingAsset asset) => Db.Set<BrandingAsset>().Add(asset);

    public void Remove(BrandingAsset asset) => Db.Set<BrandingAsset>().Remove(asset);

    public async Task SaveChangesAsync(CancellationToken cancellationToken) => await Db.SaveChangesAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (db is not null)
        {
            await db.DisposeAsync();
        }
    }

    private ITenantDbContext Db => db ?? throw new InvalidOperationException("Find the asset before changing it.");

    private async Task<ITenantDbContext> DbAsync(CancellationToken cancellationToken) =>
        db ??= await databases.CreateAsync(cancellationToken);
}
