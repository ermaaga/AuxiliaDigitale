using Auxilia.Application.Abstractions.Realtime;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Auxilia.Infrastructure.Realtime;

/// <inheritdoc cref="IRealtimeNotifier"/>
internal sealed class SignalRRealtimeNotifier(
    IHubContext<NotificationsHub> hub, ITenantContext tenantContext, ILogger<SignalRRealtimeNotifier> logger) : IRealtimeNotifier
{
    public Task ToUserAsync(Guid userId, string eventName, object payload, CancellationToken cancellationToken) =>
        SendAsync(RealtimeGroups.User(Slug, userId), eventName, payload, cancellationToken);

    public Task ToRoleAsync(TenantRole role, string eventName, object payload, CancellationToken cancellationToken) =>
        SendAsync(RealtimeGroups.Role(Slug, role), eventName, payload, cancellationToken);

    public Task ToSessionAsync(Guid sessionId, string eventName, object payload, CancellationToken cancellationToken) =>
        SendAsync(RealtimeGroups.Session(Slug, sessionId), eventName, payload, cancellationToken);

    public Task ToTenantAsync(string eventName, object payload, CancellationToken cancellationToken) =>
        SendAsync(RealtimeGroups.Tenant(Slug), eventName, payload, cancellationToken);

    private string Slug => tenantContext.Tenant.Slug;

    private async Task SendAsync(string group, string eventName, object payload, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(payload);

        try
        {
            await hub.Clients.Group(group).SendAsync(eventName, payload, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Best effort: clients re-read their state; the operation that produced the event stays successful.
            Log.Notifications.RealtimePushFailed(logger, exception, eventName, group);
        }
    }
}

/// <summary>Processes without SignalR (auxctl): nothing is connected, nothing to push.</summary>
internal sealed class NullRealtimeNotifier : IRealtimeNotifier
{
    public Task ToUserAsync(Guid userId, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ToRoleAsync(TenantRole role, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ToSessionAsync(Guid sessionId, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ToTenantAsync(string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class RealtimeRegistration
{
    public const string HubPath = "/hubs/notifications";

    /// <summary>Redis channel prefix of the SignalR backplane (skill auxilia-caching).</summary>
    public const string BackplaneChannelPrefix = "auxilia:signalr";

    /// <summary>
    /// SignalR server services and <see cref="IRealtimeNotifier"/>. With Redis, pushes and group membership go through
    /// the backplane, so every Api node and the Worker reach every connection; without it pushes reach only the
    /// connections of this process (a Worker without Redis cannot push).
    /// </summary>
    public static IServiceCollection AddRealtime(this IServiceCollection services, string? redisConnectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        var signalR = services.AddSignalR();
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            signalR.AddStackExchangeRedis(redisConnectionString, options =>
            {
                options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal(BackplaneChannelPrefix);
                options.Configuration.AbortOnConnectFail = false;
            });
        }

        services.Replace(ServiceDescriptor.Scoped<IRealtimeNotifier, SignalRRealtimeNotifier>());
        return services;
    }
}
