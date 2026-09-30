namespace Auxilia.Application.Abstractions.Caching;

/// <summary>
/// Two-level cache for reference data (skill auxilia-caching): L1 in memory on each node, L2 in Redis/Valkey when
/// configured. Only reference data (settings, translations, modules, permissions, lookups, catalog tenant lookup):
/// never decrypted secrets, credentials, files or lists of personal data. Values are immutable, serializable DTOs.
/// </summary>
public interface IReferenceDataCache
{
    Task<T> GetOrCreateAsync<T>(CacheEntry entry, Func<CancellationToken, Task<T>> load, CancellationToken cancellationToken);

    /// <summary>
    /// Removes every entry with <paramref name="tag"/> here and in Redis, then tells the other nodes to evict their L1
    /// (<c>PUBLISH auxilia:invalidate</c>). Call it after the change is committed (<c>IOperationScope.OnCommitted</c>).
    /// </summary>
    Task InvalidateAsync(string tag, CancellationToken cancellationToken);
}

/// <summary>A cache key with its tags and lifetimes (L1 = <see cref="LocalExpiration"/>, L2 = <see cref="Expiration"/>).</summary>
public sealed record CacheEntry(string Key, IReadOnlyList<string> Tags, TimeSpan LocalExpiration, TimeSpan Expiration)
{
    /// <summary>Reference data default: 60 s in memory, 30 min in Redis (ARCHITECTURE §7.3).</summary>
    public static CacheEntry ReferenceData(string key, params IReadOnlyList<string> tags) =>
        new(key, tags, TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(30));
}

/// <summary>
/// Key and tag formats. Tenant data: <c>t:{slug}:{module}:{entity}:{variant}</c>, tag <c>t:{slug}:{module}</c>.
/// Platform data shared by every tenant: <c>platform:{module}:…</c>, tag <c>platform:{module}</c>. Catalog lookups:
/// <c>catalog:{entity}:…</c>.
/// </summary>
public static class CacheKeys
{
    public static string Tenant(string slug, string module, string entity, string variant) => $"t:{slug}:{module}:{entity}:{variant}";

    public static string Platform(string module, string entity, string variant) => $"platform:{module}:{entity}:{variant}";

    public static string Catalog(string entity, string variant) => $"catalog:{entity}:{variant}";
}

/// <inheritdoc cref="CacheKeys"/>
public static class CacheTags
{
    /// <summary>Catalog tenant lookups (slug and host → tenant).</summary>
    public const string CatalogTenants = "catalog:tenants";

    public static string Tenant(string slug, string module) => $"t:{slug}:{module}";

    public static string Platform(string module) => $"platform:{module}";
}
