using System.Security.Claims;

using Auxilia.Application.Abstractions.Realtime;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Diagnostics;
using Auxilia.Infrastructure.Security.Tokens;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Auxilia.Infrastructure.Realtime;

/// <summary>
/// The realtime hub of the tenant app (<c>/hubs/notifications</c>, F16/F17). Server-to-client only: a connection joins
/// the groups of its tenant, user, roles and session (from the access token; the tenant is resolved and checked
/// active by the tenant middleware) and receives <c>Contracts.Realtime.RealtimeEvents</c>. Lives here, not in the Api,
/// because the Worker pushes through the same hub type over the Redis backplane.
/// </summary>
[Authorize]
public sealed class NotificationsHub : Hub
{
    private readonly ILogger<NotificationsHub> logger;

    public NotificationsHub(ILogger<NotificationsHub> logger) => this.logger = logger;

    public override async Task OnConnectedAsync()
    {
        var user = Context.User ?? new ClaimsPrincipal();
        var tenant = Context.GetHttpContext()?.RequestServices.GetService<ITenantContext>()?.Current;
        var reason = tenant switch
        {
            null => "NoTenant",
            { IsActive: false } => "TenantNotActive",
            _ when !Guid.TryParse(user.FindFirstValue(TokenClaims.Subject), out _) => "NoUser",
            _ when !Guid.TryParse(user.FindFirstValue(TokenClaims.Session), out _) => "NoSession",
            _ when !string.Equals(user.FindFirstValue(TokenClaims.Tenant), tenant.Slug, StringComparison.Ordinal) => "TenantMismatch",
            _ => null,
        };

        if (reason is not null)
        {
            Log.Notifications.RealtimeConnectionRejected(logger, Context.ConnectionId, reason);
            Context.Abort();
            return;
        }

        var slug = tenant!.Slug;
        var userId = Guid.Parse(user.FindFirstValue(TokenClaims.Subject)!);
        var sessionId = Guid.Parse(user.FindFirstValue(TokenClaims.Session)!);
        var groups = new List<string>
        {
            RealtimeGroups.Tenant(slug),
            RealtimeGroups.User(slug, userId),
            RealtimeGroups.Session(slug, sessionId),
        };
        groups.AddRange(user.FindAll(TokenClaims.Role)
            .Select(claim => Enum.TryParse<TenantRole>(claim.Value, ignoreCase: false, out var role) ? RealtimeGroups.Role(slug, role) : null)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal));

        foreach (var group in groups)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
        }

        Log.Notifications.RealtimeConnected(logger, Context.ConnectionId, userId);
        await base.OnConnectedAsync();
    }
}
