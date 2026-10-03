using System.Text.Json;

namespace Auxilia.Contracts.Scheduling;

/// <summary>
/// Staff schedule an appointment (F13, already approved). <c>date</c> and <c>time</c> are local to the tenant time zone
/// and must be in the future (Q20); <c>durationMinutes</c> defaults to 60 and <c>showInGlobalCalendar</c> to true.
/// Employees schedule for the clients in their charge and for themselves; Administrators for any client, with
/// <c>employeeUserId</c> or else the employee in charge of the client. <c>customFields</c>: entity <c>appointment</c> (F20).
/// </summary>
public sealed record ScheduleAppointmentRequest(
    Guid ClientId,
    Guid? EmployeeUserId,
    DateOnly Date,
    TimeOnly Time,
    int? DurationMinutes,
    string? Notes,
    bool? ShowInGlobalCalendar,
    JsonElement? CustomFields);

/// <summary>A client asks for an appointment with any active employee (Q22): Pending until the employee answers.</summary>
public sealed record RequestAppointmentRequest(Guid EmployeeUserId, DateOnly Date, TimeOnly Time, int? DurationMinutes, string? Notes);

/// <summary>Moves or edits an open appointment (staff; also by drag &amp; drop). A new start must be in the future.</summary>
public sealed record UpdateAppointmentRequest(
    DateOnly Date,
    TimeOnly Time,
    int DurationMinutes,
    string? Notes,
    bool ShowInGlobalCalendar,
    JsonElement? CustomFields);

/// <summary>An optional note (at most 500 characters) written in the status history.</summary>
public sealed record ChangeAppointmentStatusRequest(string? Note);

public sealed record AppointmentClientResponse(Guid Id, string FullName);

public sealed record AppointmentEmployeeResponse(Guid UserId, string FullName);

/// <summary>A user of the timeline (name empty when unknown).</summary>
public sealed record AppointmentUserResponse(Guid UserId, string FullName);

/// <summary>A step of the timeline; <c>fromStatus</c> is null for the creation.</summary>
public sealed record AppointmentStatusChangeResponse(string? FromStatus, string ToStatus, DateTimeOffset ChangedAt, AppointmentUserResponse? ChangedBy, string? Note);

/// <summary>
/// An appointment in the lists and calendars: <c>startsAt</c>/<c>endsAt</c> are instants, <c>date</c>/<c>time</c> the
/// start in the tenant time zone; <c>status</c> <c>Pending</c>, <c>Approved</c>, <c>Rejected</c>, <c>Completed</c> or
/// <c>Cancelled</c> (Q19).
/// </summary>
public sealed record AppointmentListItemResponse(
    Guid Id,
    AppointmentClientResponse Client,
    AppointmentEmployeeResponse Employee,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateOnly Date,
    TimeOnly Time,
    int DurationMinutes,
    string Status,
    bool ShowInGlobalCalendar,
    bool RequestedByClient,
    JsonElement CustomFields);

/// <summary>
/// An appointment with its timeline and what the caller may do. <c>hasConflict</c> (staff only, false for clients):
/// another open appointment of the same employee overlaps it.
/// </summary>
public sealed record AppointmentResponse(
    Guid Id,
    AppointmentClientResponse Client,
    AppointmentEmployeeResponse Employee,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateOnly Date,
    TimeOnly Time,
    int DurationMinutes,
    string Status,
    string? Notes,
    bool ShowInGlobalCalendar,
    bool RequestedByClient,
    JsonElement CustomFields,
    IReadOnlyList<AppointmentStatusChangeResponse> History,
    bool HasConflict,
    bool CanManage,
    bool CanCancel,
    bool CanDelete);

/// <summary>The appointments of a calendar range, with the tenant time zone the dates are in.</summary>
public sealed record AppointmentCalendarResponse(string TimeZone, IReadOnlyList<AppointmentListItemResponse> Items);

/// <summary>An open appointment of the same employee that overlaps the slot asked (staff warning, F13).</summary>
public sealed record AppointmentConflictResponse(Guid Id, string ClientName, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Status);

/// <summary>An employee a client may ask for (Q22: all active employees, the one in charge marked).</summary>
public sealed record AppointmentEmployeeChoiceResponse(Guid UserId, string FullName, bool InCharge);

/// <summary>
/// The appointment grid (F13): what the caller may see (Administrators everything, employees their own and those on the
/// global calendar, clients their own). <c>clientId</c> / <c>employeeUserId</c> narrow it (client 360°, an employee);
/// <c>from</c>/<c>to</c> are local days of the tenant, both included; <c>sort</c> one of <c>startsAt</c> (default,
/// descending), <c>status</c>, <c>client</c>, <c>employee</c>; <c>-</c> for descending.
/// </summary>
public sealed record AppointmentListQuery(
    Guid? ClientId,
    Guid? EmployeeUserId,
    string? Status,
    DateOnly? From,
    DateOnly? To,
    string? Sort,
    int Page,
    int PageSize);

/// <summary>
/// A calendar range (month, week, day or list views; at most 62 local days, both included, at most 1000 items).
/// <c>global</c> (staff): the shared calendar — appointments with <c>showInGlobalCalendar</c> still open, of every
/// employee; otherwise the caller's own (Administrators: every appointment, narrowed by <c>clientId</c> /
/// <c>employeeUserId</c>).
/// </summary>
public sealed record AppointmentCalendarQuery(DateOnly From, DateOnly To, bool Global, Guid? ClientId, Guid? EmployeeUserId);

/// <summary>A slot to check before saving (staff): the open appointments of the employee that overlap it.</summary>
public sealed record AppointmentConflictQuery(Guid EmployeeUserId, DateOnly Date, TimeOnly Time, int DurationMinutes, Guid? ExcludeId);
