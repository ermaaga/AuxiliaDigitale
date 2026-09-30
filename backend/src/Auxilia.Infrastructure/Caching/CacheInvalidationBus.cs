using Auxilia.Diagnostics;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using StackExchange.Redis;

namespace Auxilia.Infrastructure.Caching;

/// <summary>Tells the other Api/Worker nodes to evict their in-memory (L1) entries of a tag (ARCHITECTURE §7.3).</summary>
internal interface ICacheInvalidationBus
{
    Task PublishAsync(string tag, CancellationToken cancellationToken);
}

/// <summary>Single node without Redis: nothing to tell.</summary>
internal sealed class NullCacheInvalidationBus : ICacheInvalidationBus
{
    public Task PublishAsync(string tag, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// <c>PUBLISH auxilia:invalidate {node}|{tag}</c> after an invalidation, and a subscription that evicts the tag locally
/// when another node publishes it. If Redis is down the other nodes' L1 entries expire by themselves (≤ 60 s).
/// </summary>
internal sealed class RedisCacheInvalidationBus : ICacheInvalidationBus, IHostedService
{
    public static readonly RedisChannel Channel = RedisChannel.Literal("auxilia:invalidate");

    private readonly RedisConnection connection;
    private readonly HybridCache cache;
    private readonly ILogger<RedisCacheInvalidationBus> logger;
    private readonly string nodeId = Guid.NewGuid().ToString("N");
    private int subscribed;

    public RedisCacheInvalidationBus(RedisConnection connection, HybridCache cache, ILogger<RedisCacheInvalidationBus> logger)
    {
        this.connection = connection;
        this.cache = cache;
        this.logger = logger;
    }

    public async Task PublishAsync(string tag, CancellationToken cancellationToken)
    {
        try
        {
            var multiplexer = await connection.GetAsync();
            await multiplexer.GetSubscriber().PublishAsync(Channel, nodeId + "|" + tag);
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            Log.Cache.InvalidationPublishFailed(logger, exception, tag);
        }
    }

    /// <summary>
    /// Subscribes without delaying the host start. If Redis is down the subscription is retried each time the
    /// connection is restored; meanwhile this node's L1 entries expire by themselves.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var multiplexer = await connection.GetAsync();
        multiplexer.ConnectionRestored += (_, _) => _ = SubscribeAsync(multiplexer);
        _ = SubscribeAsync(multiplexer);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal bool IsSubscribed => Volatile.Read(ref subscribed) == 1;

    internal string NodeId => nodeId;

    private async Task SubscribeAsync(IConnectionMultiplexer multiplexer)
    {
        if (Interlocked.CompareExchange(ref subscribed, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await multiplexer.GetSubscriber().SubscribeAsync(Channel, (_, message) => OnMessage(message));
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            Volatile.Write(ref subscribed, 0);
            Log.Cache.InvalidationSubscriptionFailed(logger, exception);
        }
    }

    internal void OnMessage(RedisValue message)
    {
        var text = message.ToString();
        var separator = text.IndexOf('|', StringComparison.Ordinal);
        if (separator <= 0 || separator == text.Length - 1 || text.AsSpan(0, separator).SequenceEqual(nodeId))
        {
            return;
        }

        // Fire and forget: the handler runs on the multiplexer's thread and must not block it.
        _ = cache.RemoveByTagAsync(text[(separator + 1)..]).AsTask();
    }
}
