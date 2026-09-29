using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Auxilia.Persistence.Catalog.Tenancy;

/// <summary>
/// Tenants read from the Catalog with a short in-memory cache: status changes are seen within
/// <see cref="CacheDuration"/> (the distributed cache with immediate invalidation arrives with P1-10).
/// Unknown hosts are cached too, so API hosts are not looked up on every request.
/// </summary>
internal sealed class CatalogTenantDirectory : ITenantDirectory
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly CatalogDbContext catalog;
    private readonly IMemoryCache cache;

    public CatalogTenantDirectory(CatalogDbContext catalog, IMemoryCache cache)
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

        var key = "catalog:tenant:slug:" + slug;
        if (cache.TryGetValue(key, out TenantInfo? cached))
        {
            return cached;
        }

        var tenant = await Project(catalog.Tenants.Where(item => item.Slug == slug)).SingleOrDefaultAsync(cancellationToken);
        if (tenant is not null)
        {
            cache.Set(key, tenant, CacheDuration);
        }

        return tenant;
    }

    public async Task<TenantInfo?> FindByHostAsync(string host, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        var normalized = host.Trim().ToLowerInvariant();
        var key = "catalog:tenant:host:" + normalized;
        if (cache.TryGetValue(key, out TenantInfo? cached))
        {
            return cached;
        }

        var tenantIds = catalog.TenantDomains.Where(domain => domain.Host == normalized).Select(domain => domain.TenantId);
        var tenant = await Project(catalog.Tenants.Where(item => tenantIds.Contains(item.Id))).SingleOrDefaultAsync(cancellationToken);
        cache.Set(key, tenant, CacheDuration);

        return tenant;
    }

    public Task<string?> GetProtectedConnectionStringAsync(Guid tenantId, CancellationToken cancellationToken) =>
        catalog.Tenants.AsNoTracking()
            .Where(item => item.Id == tenantId)
            .Select(item => item.ConnectionSecret)
            .SingleOrDefaultAsync(cancellationToken);

    private static IQueryable<TenantInfo> Project(IQueryable<Tenant> tenants) =>
        tenants.AsNoTracking().Select(item => new TenantInfo(item.Id, item.Slug, item.Status, item.DefaultLanguage, item.TimeZone));
}
