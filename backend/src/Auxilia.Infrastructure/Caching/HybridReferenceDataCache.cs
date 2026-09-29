using Auxilia.Application.Abstractions.Caching;

using Microsoft.Extensions.Caching.Hybrid;

namespace Auxilia.Infrastructure.Caching;

/// <summary><see cref="IReferenceDataCache"/> on <see cref="HybridCache"/> (L1 memory, L2 Redis when configured).</summary>
internal sealed class HybridReferenceDataCache : IReferenceDataCache
{
    private readonly HybridCache cache;
    private readonly ICacheInvalidationBus bus;

    public HybridReferenceDataCache(HybridCache cache, ICacheInvalidationBus bus)
    {
        this.cache = cache;
        this.bus = bus;
    }

    public async Task<T> GetOrCreateAsync<T>(CacheEntry entry, Func<CancellationToken, Task<T>> load, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(load);

        var options = new HybridCacheEntryOptions { LocalCacheExpiration = entry.LocalExpiration, Expiration = entry.Expiration };
        return await cache.GetOrCreateAsync(
            entry.Key,
            load,
            static async (load, ct) => await load(ct),
            options,
            entry.Tags,
            cancellationToken);
    }

    public async Task InvalidateAsync(string tag, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        await cache.RemoveByTagAsync(tag, cancellationToken);
        await bus.PublishAsync(tag, cancellationToken);
    }
}
