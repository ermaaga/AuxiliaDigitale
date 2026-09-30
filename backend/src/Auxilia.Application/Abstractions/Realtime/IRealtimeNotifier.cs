using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Abstractions.Realtime;

/// <summary>
/// Pushes events to the connected clients of the current tenant (SignalR, Redis backplane across Api nodes and Worker).
/// Best effort: a failed push is logged and never fails the operation that produced it (clients catch up by reading).
/// </summary>
public interface IRealtimeNotifier
{
    Task ToUserAsync(Guid userId, string eventName, object payload, CancellationToken cancellationToken);

    Task ToRoleAsync(TenantRole role, string eventName, object payload, CancellationToken cancellationToken);

    /// <summary>Every connection opened with an access token of the session (all tabs and devices of that sign-in).</summary>
    Task ToSessionAsync(Guid sessionId, string eventName, object payload, CancellationToken cancellationToken);

    Task ToTenantAsync(string eventName, object payload, CancellationToken cancellationToken);
}

/// <summary>SignalR group names: always prefixed by the tenant slug (skill auxilia-multitenancy).</summary>
public static class RealtimeGroups
{
    public static string Tenant(string slug) => $"t:{slug}";

    public static string User(string slug, Guid userId) => $"t:{slug}:u:{userId:N}";

    public static string Role(string slug, TenantRole role) => $"t:{slug}:role:{role}";

    public static string Session(string slug, Guid sessionId) => $"t:{slug}:s:{sessionId:N}";
}
