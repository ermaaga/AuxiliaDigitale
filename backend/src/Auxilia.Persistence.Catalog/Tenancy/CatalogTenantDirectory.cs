using Auxilia.Application.Abstractions.Caching;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Catalog.Tenancy;

/// <summary>
/// Tenants read from the Catalog through the reference-data cache (<c>catalog:tenant:…</c>, tag
/// <see cref="CacheTags.CatalogTenants"/>): <see cref="LocalCacheDuration"/> in memory, <see cref="CacheDuration"/> in
/// Redis. Lifecycle changes invalidate the tag after commit, so a suspension is seen at once on every node. Unknown
/// slugs and hosts are cached too, so they do not reach the database on every request. The connection secret is
/// never cached.
/// </summary>
internal sealed class CatalogTenantDirectory : ITenantDirectory
{
    public static readonly TimeSpan LocalCacheDuration = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly CatalogDbContext catalog;
    private readonly IReferenceDataCache cache;

    public CatalogTenantDirectory(CatalogDbContext catalog, IReferenceDataCache cache)
    {
        this.catalog = catalog;
        this.cache = cache;
    }

    public async Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        if (!TenantSlug.IsValid(slug))
        {
            return null;
        }

        var lookup = await cache.GetOrCreateAsync(
            Entry("slug:" + slug),
            async ct => new TenantLookup(await Project(catalog.Tenants.Where(item => item.Slug == slug)).SingleOrDefaultAsync(ct)),
            cancellationToken);

        return lookup.Tenant;
    }

    public async Task<TenantInfo?> FindByHostAsync(string host, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        var normalized = host.Trim().ToLowerInvariant();
        var lookup = await cache.GetOrCreateAsync(
            Entry("host:" + normalized),
            async ct =>
            {
                var tenantIds = catalog.TenantDomains.Where(domain => domain.Host == normalized).Select(domain => domain.TenantId);
                return new TenantLookup(await Project(catalog.Tenants.Where(item => tenantIds.Contains(item.Id))).SingleOrDefaultAsync(ct));
            },
            cancellationToken);

        return lookup.Tenant;
    }

    public Task<string?> GetProtectedConnectionStringAsync(Guid tenantId, CancellationToken cancellationToken) =>
        catalog.Tenants.AsNoTracking()
            .Where(item => item.Id == tenantId)
            .Select(item => item.ConnectionSecret)
            .SingleOrDefaultAsync(cancellationToken);

    private static CacheEntry Entry(string variant) =>
        new(CacheKeys.Catalog("tenant", variant), [CacheTags.CatalogTenants], LocalCacheDuration, CacheDuration);

    private static IQueryable<TenantInfo> Project(IQueryable<Tenant> tenants) =>
        tenants.AsNoTracking().Select(item => new TenantInfo(item.Id, item.Slug, item.Status, item.DefaultLanguage, item.TimeZone));

    /// <summary>Cached result of a lookup, also when no tenant matches.</summary>
    internal sealed record TenantLookup(TenantInfo? Tenant);
}
