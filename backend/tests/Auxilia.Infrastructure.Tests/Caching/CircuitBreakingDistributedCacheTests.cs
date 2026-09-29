using Auxilia.Diagnostics;
using Auxilia.Infrastructure.Caching;

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Auxilia.Infrastructure.Tests.Caching;

public sealed class CircuitBreakingDistributedCacheTests : IDisposable
{
    private readonly IDistributedCache inner = Substitute.For<IDistributedCache>();
    private readonly ManualTimeProvider clock = new();
    private readonly EventCollector events = new();
    private readonly CircuitBreakingDistributedCache cache;

    public CircuitBreakingDistributedCacheTests()
    {
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(events));
        cache = new CircuitBreakingDistributedCache(inner, clock, factory.CreateLogger<CircuitBreakingDistributedCache>());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Failure_OpensTheCircuitLogsOnceAndShortCircuitsUntilItExpires()
    {
        inner.GetAsync("a", Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("redis"));

        (await cache.GetAsync("a", Ct)).ShouldBeNull();
        (await cache.GetAsync("a", Ct)).ShouldBeNull();
        await cache.SetAsync("a", [1], new DistributedCacheEntryOptions(), Ct);
        cache.Remove("a");

        cache.IsOpen.ShouldBeTrue();
        await inner.Received(1).GetAsync("a", Arg.Any<CancellationToken>());
        await inner.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>());
        inner.DidNotReceive().Remove(Arg.Any<string>());
        events.Count(EventCodes.Cache.CacheBackendUnavailable).ShouldBe(1);
    }

    [Fact]
    public async Task FirstSuccessAfterTheBreak_ClosesTheCircuitAndLogsRecovery()
    {
        inner.GetAsync("a", Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("redis"));
        await cache.GetAsync("a", Ct);

        clock.Advance(CircuitBreakingDistributedCache.BreakDuration + TimeSpan.FromSeconds(1));
        inner.GetAsync("a", Arg.Any<CancellationToken>()).Returns([42]);

        (await cache.GetAsync("a", Ct)).ShouldBe([42]);
        cache.IsOpen.ShouldBeFalse();
        events.Count(EventCodes.Cache.CacheBackendRecovered).ShouldBe(1);

        (await cache.GetAsync("a", Ct)).ShouldBe([42]);
        events.Count(EventCodes.Cache.CacheBackendRecovered).ShouldBe(1);
    }

    [Fact]
    public async Task EveryOperation_IsForwardedWhileClosed()
    {
        inner.Get("k").Returns([1]);

        cache.Get("k").ShouldBe([1]);
        cache.Set("k", [2], new DistributedCacheEntryOptions());
        cache.Refresh("k");
        cache.Remove("k");
        await cache.RefreshAsync("k", Ct);
        await cache.RemoveAsync("k", Ct);
        await cache.SetAsync("k", [3], new DistributedCacheEntryOptions(), Ct);

        inner.Received(1).Set("k", Arg.Any<byte[]>(), Arg.Any<DistributedCacheEntryOptions>());
        inner.Received(1).Refresh("k");
        inner.Received(1).Remove("k");
        await inner.Received(1).RefreshAsync("k", Arg.Any<CancellationToken>());
        await inner.Received(1).RemoveAsync("k", Arg.Any<CancellationToken>());
        events.Events.ShouldBeEmpty();
    }

    [Fact]
    public void SynchronousFailure_AlsoOpensTheCircuit()
    {
        inner.When(item => item.Set(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<DistributedCacheEntryOptions>()))
            .Do(_ => throw new InvalidOperationException("redis"));

        cache.Set("k", [1], new DistributedCacheEntryOptions());

        cache.IsOpen.ShouldBeTrue();
        cache.Get("k").ShouldBeNull();
        inner.DidNotReceive().Get(Arg.Any<string>());
    }

    public void Dispose() => events.Dispose();
}
