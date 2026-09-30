using Auxilia.Application.Abstractions.Tenancy;

namespace Auxilia.Application.Abstractions.Caching;

/// <summary>
/// One immutable snapshot of a module's reference data per tenant (ARCHITECTURE §7.3), e.g. settings, translations,
/// effective modules. The entry is tagged with the tenant module tag and the platform module tag, so a change of
/// tenant data evicts that tenant only and a change of platform data evicts every tenant. Without a tenant in scope
/// the snapshot holds platform data only.
/// </summary>
public abstract class ReferenceDataCache<T>
    where T : class
{
    private readonly IReferenceDataCache cache;
    private readonly ITenantContext tenantContext;

    protected ReferenceDataCache(IReferenceDataCache cache, ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(tenantContext);

        this.cache = cache;
        this.tenantContext = tenantContext;
    }

    /// <summary>Module segment of keys and tags, lower case (e.g. <c>configuration</c>).</summary>
    protected abstract string Module { get; }

    /// <summary>Entity segment of the key (e.g. <c>snapshot</c>).</summary>
    protected abstract string Entity { get; }

    public Task<T> GetAsync(CancellationToken cancellationToken) => GetAsync("current", cancellationToken);

    /// <param name="variant">Distinguishes snapshots of the same entity (e.g. the language of a translation bundle).</param>
    public Task<T> GetAsync(string variant, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);

        var tenant = tenantContext.Current;
        var entry = tenant is null
            ? CacheEntry.ReferenceData(CacheKeys.Platform(Module, Entity, variant), CacheTags.Platform(Module))
            : CacheEntry.ReferenceData(CacheKeys.Tenant(tenant.Slug, Module, Entity, variant), CacheTags.Tenant(tenant.Slug, Module), CacheTags.Platform(Module));

        return cache.GetOrCreateAsync(entry, ct => LoadAsync(tenant, variant, ct), cancellationToken);
    }

    /// <summary>Evicts the snapshots of one tenant (after a tenant-level change is committed).</summary>
    public Task InvalidateTenantAsync(string slug, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        return cache.InvalidateAsync(CacheTags.Tenant(slug, Module), cancellationToken);
    }

    /// <summary>Evicts the snapshots of every tenant (after a platform-level change is committed).</summary>
    public Task InvalidatePlatformAsync(CancellationToken cancellationToken) =>
        cache.InvalidateAsync(CacheTags.Platform(Module), cancellationToken);

    /// <param name="tenant">The tenant of the snapshot, <c>null</c> for the platform-only snapshot.</param>
    protected abstract Task<T> LoadAsync(TenantInfo? tenant, string variant, CancellationToken cancellationToken);
}
