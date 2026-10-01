using Auxilia.Domain.Configuration;

namespace Auxilia.Application.Abstractions.Configuration;

/// <summary>Branding images of the current tenant (<c>configuration.branding_assets</c>); writes join the running operation.</summary>
public interface IBrandingAssetStore
{
    /// <summary>The image with its content, tracked for a change inside the running operation.</summary>
    Task<BrandingAsset?> FindAsync(BrandingAssetKind kind, CancellationToken cancellationToken);

    /// <summary>The image with its content, read only (serving it).</summary>
    Task<BrandingAsset?> ReadAsync(BrandingAssetKind kind, CancellationToken cancellationToken);

    /// <summary>The version (hash) of every stored image, without the content.</summary>
    Task<IReadOnlyDictionary<BrandingAssetKind, string>> VersionsAsync(CancellationToken cancellationToken);

    void Add(BrandingAsset asset);

    void Remove(BrandingAsset asset);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
