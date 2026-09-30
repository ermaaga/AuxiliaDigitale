using Auxilia.Diagnostics;

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Auxilia.Infrastructure.Caching;

/// <summary>
/// The Redis L2 behind a circuit breaker (skill auxilia-caching): the first failure opens the circuit for
/// <see cref="BreakDuration"/>, logs <c>AUX-24001</c> once and makes every call a miss/no-op, so requests are served
/// from L1 and the database without waiting for Redis timeouts. The first success after that logs <c>AUX-24002</c>.
/// </summary>
internal sealed class CircuitBreakingDistributedCache : IDistributedCache
{
    public static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(30);

    private readonly IDistributedCache inner;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<CircuitBreakingDistributedCache> logger;
    private readonly Lock gate = new();
    private DateTimeOffset openUntil = DateTimeOffset.MinValue;
    private bool isBroken;

    public CircuitBreakingDistributedCache(IDistributedCache inner, TimeProvider timeProvider, ILogger<CircuitBreakingDistributedCache> logger)
    {
        this.inner = inner;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public bool IsOpen
    {
        get
        {
            lock (gate)
            {
                return timeProvider.GetUtcNow() < openUntil;
            }
        }
    }

    public byte[]? Get(string key) => Execute(() => inner.Get(key), fallback: null);

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
        ExecuteAsync(() => inner.GetAsync(key, token), fallback: null);

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
        Execute(() => { inner.Set(key, value, options); return true; }, fallback: false);

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
        ExecuteAsync(async () => { await inner.SetAsync(key, value, options, token); return true; }, fallback: false);

    public void Refresh(string key) => Execute(() => { inner.Refresh(key); return true; }, fallback: false);

    public Task RefreshAsync(string key, CancellationToken token = default) =>
        ExecuteAsync(async () => { await inner.RefreshAsync(key, token); return true; }, fallback: false);

    public void Remove(string key) => Execute(() => { inner.Remove(key); return true; }, fallback: false);

    public Task RemoveAsync(string key, CancellationToken token = default) =>
        ExecuteAsync(async () => { await inner.RemoveAsync(key, token); return true; }, fallback: false);

    private T Execute<T>(Func<T> call, T fallback)
    {
        if (IsOpen)
        {
            return fallback;
        }

        try
        {
            var result = call();
            Close();
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Trip(exception);
            return fallback;
        }
    }

    private async Task<T> ExecuteAsync<T>(Func<Task<T>> call, T fallback)
    {
        if (IsOpen)
        {
            return fallback;
        }

        try
        {
            var result = await call();
            Close();
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Trip(exception);
            return fallback;
        }
    }

    private void Trip(Exception exception)
    {
        bool first;
        lock (gate)
        {
            first = !isBroken;
            isBroken = true;
            openUntil = timeProvider.GetUtcNow() + BreakDuration;
        }

        if (first)
        {
            Log.Cache.CacheBackendUnavailable(logger, exception, BreakDuration.TotalSeconds);
        }
    }

    private void Close()
    {
        bool recovered;
        lock (gate)
        {
            recovered = isBroken;
            isBroken = false;
        }

        if (recovered)
        {
            Log.Cache.CacheBackendRecovered(logger);
        }
    }
}
