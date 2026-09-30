using Auxilia.Application.Abstractions.Caching;
using Auxilia.Diagnostics;
using Auxilia.Infrastructure.Caching;

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Testcontainers.Redis;

namespace Auxilia.Infrastructure.Tests.Caching;

/// <summary>A Valkey container shared by the Redis tests.</summary>
public sealed class ValkeyFixture : IAsyncLifetime
{
    private readonly RedisContainer container = new RedisBuilder("valkey/valkey:8-alpine").Build();

    public string ConnectionString => container.GetConnectionString();

    public ValueTask InitializeAsync() => new(container.StartAsync());

    public ValueTask DisposeAsync() => container.DisposeAsync();
}

/// <summary>
/// Reference-data cache on Redis/Valkey (skill auxilia-caching): L2 shared by the nodes, cross-node L1 invalidation,
/// and requests that keep working with Redis down.
/// </summary>
public sealed class RedisCacheTests(ValkeyFixture valkey) : IClassFixture<ValkeyFixture>
{
    private const string Tag = "t:acme:configuration";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TwoNodes_ShareL2AndInvalidationEvictsTheOtherNodesMemory()
    {
        await using var nodeA = await Node.StartAsync(valkey.ConnectionString);
        await using var nodeB = await Node.StartAsync(valkey.ConnectionString);
        var entry = CacheEntry.ReferenceData("t:acme:configuration:snapshot:" + Guid.NewGuid().ToString("N"), Tag);

        (await nodeA.Cache.GetOrCreateAsync(entry, _ => Task.FromResult(new CachedPayload("v1")), Ct)).Value.ShouldBe("v1");

        // Node B finds the value in Redis without loading it (HybridCache writes L2 in the background).
        await nodeA.WaitForL2Async(entry.Key);
        (await nodeB.Cache.GetOrCreateAsync(entry, _ => Task.FromResult(new CachedPayload("loaded by B")), Ct)).Value.ShouldBe("v1");

        await nodeA.Cache.InvalidateAsync(Tag, Ct);

        // Node B had "v1" in memory for 60 s: only the published invalidation evicts it.
        var seen = await EventuallyAsync(async () =>
            (await nodeB.Cache.GetOrCreateAsync(entry, _ => Task.FromResult(new CachedPayload("v2")), Ct)).Value);
        seen.ShouldBe("v2");

        // Node A evicted its own memory: it reads B's value from Redis or, if B's background L2 write is not done yet,
        // loads again — never the stale one.
        (await nodeA.Cache.GetOrCreateAsync(entry, _ => Task.FromResult(new CachedPayload("v3")), Ct)).Value.ShouldNotBe("v1");
    }

    [Fact]
    public async Task RedisDown_RequestsAreServedByTheLoaderAndTheCircuitLogsOnce()
    {
        await using var node = await Node.StartAsync("127.0.0.1:1,connectTimeout=500");
        var started = TimeProvider.System.GetTimestamp();

        for (var index = 0; index < 5; index++)
        {
            var entry = CacheEntry.ReferenceData("t:acme:configuration:snapshot:" + index, Tag);
            (await node.Cache.GetOrCreateAsync(entry, _ => Task.FromResult(new CachedPayload("db-" + index)), Ct)).Value.ShouldBe("db-" + index);
        }

        await node.Cache.InvalidateAsync(Tag, Ct);

        TimeProvider.System.GetElapsedTime(started).ShouldBeLessThan(TimeSpan.FromSeconds(10));
        node.Events.Count(EventCodes.Cache.CacheBackendUnavailable).ShouldBe(1);
        node.Events.Count(EventCodes.Cache.InvalidationPublishFailed).ShouldBe(1);
        (await node.CheckHealthAsync()).ShouldBe(HealthStatus.Degraded);
    }

    [Fact]
    public async Task RedisUp_HealthIsHealthy()
    {
        await using var node = await Node.StartAsync(valkey.ConnectionString);

        (await node.CheckHealthAsync()).ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task WithoutRedis_TheCacheWorksInMemory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure();
        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IReferenceDataCache>();
        var entry = CacheEntry.ReferenceData("platform:configuration:snapshot:current", "platform:configuration");

        (await cache.GetOrCreateAsync(entry, _ => Task.FromResult(new CachedPayload("v1")), Ct)).Value.ShouldBe("v1");
        (await cache.GetOrCreateAsync(entry, _ => Task.FromResult(new CachedPayload("v2")), Ct)).Value.ShouldBe("v1");
        await cache.InvalidateAsync("platform:configuration", Ct);
        (await cache.GetOrCreateAsync(entry, _ => Task.FromResult(new CachedPayload("v3")), Ct)).Value.ShouldBe("v3");
    }

    [Fact]
    public async Task InvalidationMessages_FromThisNodeOrMalformed_AreIgnored()
    {
        var hybrid = Substitute.For<HybridCache>();
        var bus = new RedisCacheInvalidationBus(new RedisConnection("127.0.0.1:1"), hybrid, NullLogger<RedisCacheInvalidationBus>.Instance);

        bus.OnMessage(bus.NodeId + "|" + Tag);
        bus.OnMessage("no-separator");
        bus.OnMessage("|" + Tag);
        bus.OnMessage("other|");
        bus.OnMessage("other-node|" + Tag);

        await hybrid.Received(1).RemoveByTagAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await hybrid.Received(1).RemoveByTagAsync(Tag, Arg.Any<CancellationToken>());
    }

    private static async Task<T> EventuallyAsync<T>(Func<Task<T>> read)
        where T : notnull
    {
        T value = default!;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            value = await read();
            if (!Equals(value, "v1"))
            {
                return value;
            }

            await Task.Delay(100, Ct);
        }

        return value;
    }

    /// <summary>One Api/Worker node: its own memory cache, the shared Redis, the invalidation subscription.</summary>
    private sealed class Node : IAsyncDisposable
    {
        private readonly ServiceProvider provider;

        private Node(ServiceProvider provider, EventCollector events)
        {
            this.provider = provider;
            Events = events;
            Cache = provider.GetRequiredService<IReferenceDataCache>();
        }

        public IReferenceDataCache Cache { get; }


        public EventCollector Events { get; }

        public static async Task<Node> StartAsync(string redis)
        {
            var events = new EventCollector();
            var services = new ServiceCollection();
            services.AddLogging(logging => logging.AddProvider(events));
            services.AddInfrastructure();
            services.AddRedisCache(redis);
            var provider = services.BuildServiceProvider();

            var bus = provider.GetRequiredService<RedisCacheInvalidationBus>();
            foreach (var hosted in provider.GetServices<IHostedService>())
            {
                await hosted.StartAsync(Ct);
            }

            for (var attempt = 0; attempt < 30 && !bus.IsSubscribed; attempt++)
            {
                await Task.Delay(100, Ct);
            }

            return new Node(provider, events);
        }

        public async Task<HealthStatus> CheckHealthAsync()
        {
            var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(Ct);
            return report.Entries[CachingRegistration.RedisHealthCheckName].Status;
        }

        public async Task WaitForL2Async(string key)
        {
            var l2 = provider.GetRequiredService<IDistributedCache>();
            for (var attempt = 0; attempt < 50 && await l2.GetAsync(key, Ct) is null; attempt++)
            {
                await Task.Delay(100, Ct);
            }
        }

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }
}
