namespace Auxilia.Contracts.Realtime;

/// <summary>
/// Events pushed on the SignalR hub <c>/hubs/notifications</c> (method names the clients subscribe to). Payloads are the
/// records below, serialised as camelCase JSON.
/// </summary>
public static class RealtimeEvents
{
    /// <summary>The session ended (logout elsewhere, single session, password reset, revocation): sign out now.</summary>
    public const string ForceLogout = "ForceLogout";

    /// <summary>A new in-app notification for the user (F16, B-19).</summary>
    public const string NotificationReceived = "NotificationReceived";

    /// <summary>A registration request arrived (F02, to the Administrators when <c>registration.notifyAdmins</c> is on).</summary>
    public const string RegistrationRequested = "RegistrationRequested";
}

/// <param name="Reason">Why the session ended: <c>Logout</c>, <c>SingleSession</c>, <c>SecurityStampChanged</c>, <c>RefreshTokenReuse</c>, <c>Revoked</c>.</param>
public sealed record ForceLogoutEvent(string Reason);

/// <param name="RegistrationId">The pending request (<c>GET /api/v1/registrations/{id}</c>).</param>
public sealed record RegistrationRequestedEvent(Guid RegistrationId, string FullName);
