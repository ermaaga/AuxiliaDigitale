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

    /// <summary>A document uploaded by the user was checked (F14, Q13: the uploader is told, not the client).</summary>
    public const string DocumentProcessed = "DocumentProcessed";

    /// <summary>An appointment of the user changed (F13): told to the other party (client or employee), never to the actor.</summary>
    public const string AppointmentChanged = "AppointmentChanged";

    /// <summary>A request the user takes part in changed (F15): new, replied or closed; never told to the actor.</summary>
    public const string RequestChanged = "RequestChanged";
}

/// <param name="Reason">Why the session ended: <c>Logout</c>, <c>SingleSession</c>, <c>SecurityStampChanged</c>, <c>RefreshTokenReuse</c>, <c>Revoked</c>.</param>
public sealed record ForceLogoutEvent(string Reason);

/// <param name="RegistrationId">The pending request (<c>GET /api/v1/registrations/{id}</c>).</param>
public sealed record RegistrationRequestedEvent(Guid RegistrationId, string FullName);

/// <param name="Status"><c>Available</c> or <c>Damaged</c>.</param>
public sealed record DocumentProcessedEvent(Guid DocumentId, string FileName, string Status);

/// <param name="Change"><c>Scheduled</c>, <c>Requested</c>, <c>Updated</c>, <c>Approved</c>, <c>Rejected</c>, <c>Completed</c>, <c>Cancelled</c> or <c>Deleted</c>.</param>
public sealed record AppointmentChangedEvent(Guid AppointmentId, string Change, DateTimeOffset StartsAt);

/// <param name="Change"><c>Created</c>, <c>Replied</c> or <c>Closed</c>.</param>
public sealed record RequestChangedEvent(Guid RequestId, string Change, string Subject);
