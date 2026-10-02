using Auxilia.Application.Abstractions.Logging;
using Auxilia.Application.Platform;
using Auxilia.Diagnostics;
using Auxilia.Infrastructure.Caching;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using StackExchange.Redis;

namespace Auxilia.Infrastructure.Logging;

/// <summary>Single node without Redis: the node that made the change has already applied it.</summary>
internal sealed class NullTenantLogLevelBroadcast : ITenantLogLevelBroadcast
{
    public Task PublishAsync(string tenantSlug, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// <c>PUBLISH auxilia:log-levels {node}|{slug}</c> after a change (D-28), and a subscription that reloads the tenant's
/// level from the Catalog when another node publishes it. The message carries no level: the Catalog is the source.
/// </summary>
internal sealed class RedisTenantLogLevelBus : ITenantLogLevelBroadcast, IHostedService
{
    public static readonly RedisChannel Channel = RedisChannel.Literal("auxilia:log-levels");

    private readonly RedisConnection connection;
    private readonly IServiceScopeFactory scopes;
    private readonly ILogger<RedisTenantLogLevelBus> logger;
    private readonly string nodeId = Guid.NewGuid().ToString("N");
    private int subscribed;

    public RedisTenantLogLevelBus(RedisConnection connection, IServiceScopeFactory scopes, ILogger<RedisTenantLogLevelBus> logger)
    {
        this.connection = connection;
        this.scopes = scopes;
        this.logger = logger;
    }

    public async Task PublishAsync(string tenantSlug, CancellationToken cancellationToken)
    {
        try
        {
            var multiplexer = await connection.GetAsync();
            await multiplexer.GetSubscriber().PublishAsync(Channel, nodeId + "|" + tenantSlug);
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            Log.Tenancy.LogLevelSyncFailed(logger, exception, "publish");
        }
    }

    /// <summary>Subscribes without delaying the host start, again each time the connection is restored.</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var multiplexer = await connection.GetAsync();
        multiplexer.ConnectionRestored += (_, _) => _ = SubscribeAsync(multiplexer);
        _ = SubscribeAsync(multiplexer);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal bool IsSubscribed => Volatile.Read(ref subscribed) == 1;

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
            Log.Tenancy.LogLevelSyncFailed(logger, exception, "subscribe");
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
        _ = RefreshAsync(text[(separator + 1)..]);
    }

    internal async Task RefreshAsync(string tenantSlug)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ITenantLogLevelSync>().RefreshAsync(tenantSlug, CancellationToken.None);
        }
#pragma warning disable CA1031 // Background refresh: any failure is logged with its code and the node keeps its levels (ADR 0012).
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Log.Tenancy.LogLevelSyncFailed(logger, exception, "refresh");
        }
    }
}

/// <summary>Loads the running debug overrides of every tenant from the Catalog when the node starts (D-28).</summary>
internal sealed class TenantLogLevelStartup(IServiceScopeFactory scopes, ILogger<TenantLogLevelStartup> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ITenantLogLevelSync>().LoadAllAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The node starts with the default level for every tenant rather than not at all.
            Log.Tenancy.LogLevelSyncFailed(logger, exception, "startup");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class TenantLogLevelRegistration
{
    /// <summary>Api and Worker: apply the per-tenant log levels of the Catalog at start (changes arrive through the bus).</summary>
    public static IServiceCollection AddTenantLogLevelSync(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHostedService<TenantLogLevelStartup>();
        return services;
    }
}
