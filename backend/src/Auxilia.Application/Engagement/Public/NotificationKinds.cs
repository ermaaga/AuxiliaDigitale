namespace Auxilia.Application.Engagement.Public;

/// <summary>
/// The kinds of notification (F16) and the page each opens (Q12: one page per concept, so the same route works for
/// every role; the page decides what the reader may do). Texts: <c>notifications.{kind}.title|message</c>.
/// </summary>
public static class NotificationKinds
{
    public const string AppointmentScheduled = "appointment.scheduled";
    public const string AppointmentRequested = "appointment.requested";
    public const string AppointmentUpdated = "appointment.updated";
    public const string AppointmentApproved = "appointment.approved";
    public const string AppointmentRejected = "appointment.rejected";
    public const string AppointmentCompleted = "appointment.completed";
    public const string AppointmentCancelled = "appointment.cancelled";
    public const string AppointmentDeleted = "appointment.deleted";
    public const string RequestCreated = "request.created";
    public const string RequestReplied = "request.replied";
    public const string RequestClosed = "request.closed";
    public const string RegistrationRequested = "registration.requested";
    public const string ExportReady = "export.ready";

    public static IReadOnlyList<string> All { get; } =
    [
        AppointmentScheduled, AppointmentRequested, AppointmentUpdated, AppointmentApproved, AppointmentRejected,
        AppointmentCompleted, AppointmentCancelled, AppointmentDeleted, RequestCreated, RequestReplied, RequestClosed,
        RegistrationRequested, ExportReady,
    ];

    /// <summary>The route (without the tenant) a notification opens; <c>null</c> when the record is gone.</summary>
    public static string? Link(string kind, Guid? entityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (entityId is not { } id)
        {
            return null;
        }

        return kind switch
        {
            AppointmentDeleted => "/appointments",
            _ when kind.StartsWith("appointment.", StringComparison.Ordinal) => $"/appointments?open={id}",
            _ when kind.StartsWith("request.", StringComparison.Ordinal) => $"/requests?open={id}",
            // Registrations have no staff page yet (API only, D-14): the notification opens nothing.
            ExportReady => $"/exports?download={id}",
            _ => null,
        };
    }
}
