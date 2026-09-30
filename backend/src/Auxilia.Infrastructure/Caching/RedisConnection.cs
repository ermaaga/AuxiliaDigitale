using StackExchange.Redis;

namespace Auxilia.Infrastructure.Caching;

/// <summary>
/// The shared Redis/Valkey connection (L2 cache, invalidation channel; later SignalR backplane and locks). Connects
/// lazily and never fails the host when Redis is down: the multiplexer keeps reconnecting in the background.
/// </summary>
internal sealed class RedisConnection : IAsyncDisposable
{
    private readonly Lazy<Task<IConnectionMultiplexer>> multiplexer;

    public RedisConnection(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = Math.Min(options.ConnectTimeout, 2000);
        options.AsyncTimeout = Math.Min(options.AsyncTimeout, 1000);
        options.SyncTimeout = Math.Min(options.SyncTimeout, 1000);
        options.ClientName ??= "auxilia";

        multiplexer = new Lazy<Task<IConnectionMultiplexer>>(async () => await ConnectionMultiplexer.ConnectAsync(options));
    }

    public Task<IConnectionMultiplexer> GetAsync() => multiplexer.Value;

    public async ValueTask DisposeAsync()
    {
        if (multiplexer.IsValueCreated)
        {
            await (await multiplexer.Value).DisposeAsync();
        }
    }
}
