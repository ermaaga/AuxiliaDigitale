using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Scheduling;

/// <summary>
/// Where an appointment is (F13, Q19): Pending (requested by the client) → Approved or Rejected; Approved → Completed;
/// Pending or Approved → Cancelled. Rejected, Completed and Cancelled are terminal.
/// </summary>
public enum AppointmentStatus
{
    Pending,
    Approved,
    Rejected,
    Completed,
    Cancelled,
}

/// <summary>When and how long an appointment is, and what goes with it.</summary>
public sealed record AppointmentSlot(DateTimeOffset StartsAt, int DurationMinutes, string? Notes, bool ShowInGlobalCalendar, string CustomFields);

/// <summary>
/// An appointment of a client with an employee (<c>scheduling.appointments</c>, legacy <c>Appointment</c>, F13). Staff
/// schedule it already approved; a client requests it (Pending, legacy <c>IsCreatedByFinalUser</c>). Only the future is
/// accepted when it is scheduled, requested or moved (Q20: an instant, so the tenant time zone only shapes the input).
/// Every status change is kept in <c>appointment_status_history</c>; deletes are soft.
/// </summary>
public sealed class Appointment : AggregateRoot<Guid>, IAuditable, ISoftDeletable
{
    public const int DefaultDurationMinutes = 60;
    public const int MinDurationMinutes = 5;

    /// <summary>One day: longer meetings are several appointments.</summary>
    public const int MaxDurationMinutes = 24 * 60;

    public const int NotesMaxLength = 1000;
    public const int NoteMaxLength = 500;

    private readonly List<AppointmentStatusChange> history = [];

    private Appointment(Guid id, Guid clientId, Guid employeeUserId, AppointmentSlot slot, AppointmentStatus status, bool requestedByClient)
        : base(id)
    {
        ClientId = clientId;
        EmployeeUserId = employeeUserId;
        StartsAt = slot.StartsAt;
        DurationMinutes = slot.DurationMinutes;
        EndsAt = slot.StartsAt.AddMinutes(slot.DurationMinutes);
        Notes = Text(slot.Notes);
        ShowInGlobalCalendar = slot.ShowInGlobalCalendar;
        CustomFields = slot.CustomFields;
        Status = status;
        RequestedByClient = requestedByClient;
    }

    private Appointment()
    {
        CustomFields = string.Empty;
    }

    /// <summary>The client (its person id, Directory).</summary>
    public Guid ClientId { get; private set; }

    /// <summary>The employee (user id, Directory).</summary>
    public Guid EmployeeUserId { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public int DurationMinutes { get; private set; }

    /// <summary>Start plus duration, stored for the range and overlap queries.</summary>
    public DateTimeOffset EndsAt { get; private set; }

    public AppointmentStatus Status { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>Legacy <c>ShowInGlobalCalendare</c>: shown on the shared staff calendar and the employee dashboard.</summary>
    public bool ShowInGlobalCalendar { get; private set; }

    /// <summary>The client asked for it (legacy <c>IsCreatedByFinalUser</c>): the employee is the one told.</summary>
    public bool RequestedByClient { get; private set; }

    /// <summary>Custom field values (JSON object, already validated, F20).</summary>
    public string CustomFields { get; private set; }

    /// <summary>The timeline, in the order of the changes.</summary>
    public IReadOnlyList<AppointmentStatusChange> History => history.OrderBy(change => change.Sequence).ToArray();

    /// <summary>Whether it still takes the employee's time (conflicts and calendars): Pending or Approved.</summary>
    public static bool IsOpen(AppointmentStatus status) => status is AppointmentStatus.Pending or AppointmentStatus.Approved;

    /// <summary>Staff schedule it (legacy employee modal): already Approved.</summary>
    public static Result<Appointment> Schedule(Guid id, Guid clientId, Guid employeeUserId, AppointmentSlot slot, Guid? actorUserId, DateTimeOffset now) =>
        Create(id, clientId, employeeUserId, slot, AppointmentStatus.Approved, requestedByClient: false, actorUserId, now);

    /// <summary>A client asks for it (legacy client page): Pending until the employee approves or rejects it.</summary>
    public static Result<Appointment> Request(Guid id, Guid clientId, Guid employeeUserId, AppointmentSlot slot, Guid? actorUserId, DateTimeOffset now) =>
        Create(id, clientId, employeeUserId, slot, AppointmentStatus.Pending, requestedByClient: true, actorUserId, now);

    /// <summary>
    /// Moves or edits an open appointment (date, time, duration, notes, global flag, custom fields; drag &amp; drop). A new
    /// start must be in the future; keeping the start of an appointment already begun is allowed.
    /// </summary>
    public Result Update(AppointmentSlot slot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot.CustomFields);

        if (!IsOpen(Status))
        {
            return Errors.Scheduling.AppointmentClosed();
        }

        if (Check(slot, now, checkFuture: slot.StartsAt != StartsAt) is { Count: > 0 } errors)
        {
            return Errors.Scheduling.AppointmentInvalid(errors);
        }

        StartsAt = slot.StartsAt;
        DurationMinutes = slot.DurationMinutes;
        EndsAt = slot.StartsAt.AddMinutes(slot.DurationMinutes);
        Notes = Text(slot.Notes);
        ShowInGlobalCalendar = slot.ShowInGlobalCalendar;
        CustomFields = slot.CustomFields;
        return Result.Success();
    }

    /// <summary>Pending → Approved.</summary>
    public Result Approve(Guid? actorUserId, string? note, DateTimeOffset now) =>
        Status == AppointmentStatus.Pending ? Move(AppointmentStatus.Approved, actorUserId, note, now) : Errors.Scheduling.AppointmentNotPending();

    /// <summary>Pending → Rejected.</summary>
    public Result Reject(Guid? actorUserId, string? note, DateTimeOffset now) =>
        Status == AppointmentStatus.Pending ? Move(AppointmentStatus.Rejected, actorUserId, note, now) : Errors.Scheduling.AppointmentNotPending();

    /// <summary>Approved → Completed (legacy: with confirmation, also before its time).</summary>
    public Result Complete(Guid? actorUserId, string? note, DateTimeOffset now) =>
        Status == AppointmentStatus.Approved ? Move(AppointmentStatus.Completed, actorUserId, note, now) : Errors.Scheduling.AppointmentNotApproved();

    /// <summary>Pending or Approved → Cancelled (Q21: what the client's delete becomes).</summary>
    public Result Cancel(Guid? actorUserId, string? note, DateTimeOffset now) =>
        IsOpen(Status) ? Move(AppointmentStatus.Cancelled, actorUserId, note, now) : Errors.Scheduling.AppointmentClosed();

    /// <summary>
    /// Legacy import (E-05): an appointment as the legacy application left it, past ones included, with one history row
    /// for its status at <paramref name="createdAt"/>.
    /// </summary>
    public static Result<Appointment> ImportLegacy(
        Guid id, Guid clientId, Guid employeeUserId, AppointmentSlot slot, AppointmentStatus status, bool requestedByClient, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot.CustomFields);

        if (Check(slot, createdAt, checkFuture: false) is { Count: > 0 } errors)
        {
            return Errors.Scheduling.AppointmentInvalid(errors);
        }

        var imported = new Appointment(id, clientId, employeeUserId, slot, status, requestedByClient);
        imported.history.Add(new AppointmentStatusChange(Guid.CreateVersion7(), id, 1, null, status, createdAt, null, null));
        return imported;
    }

    /// <summary>Legacy import (E-05), a later run: the legacy time and status win; a different status adds a history row.</summary>
    /// <returns>Whether something changed.</returns>
    public bool ApplyLegacyState(AppointmentSlot slot, AppointmentStatus status, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(slot);

        var changed = StartsAt != slot.StartsAt || DurationMinutes != slot.DurationMinutes || Notes != Text(slot.Notes) || ShowInGlobalCalendar != slot.ShowInGlobalCalendar;
        StartsAt = slot.StartsAt;
        DurationMinutes = slot.DurationMinutes;
        EndsAt = slot.StartsAt.AddMinutes(slot.DurationMinutes);
        Notes = Text(slot.Notes);
        ShowInGlobalCalendar = slot.ShowInGlobalCalendar;
        if (Status != status)
        {
            history.Add(new AppointmentStatusChange(Guid.CreateVersion7(), Id, history.Count + 1, Status, status, at, null, null));
            Status = status;
            changed = true;
        }

        return changed;
    }

    private static Result<Appointment> Create(
        Guid id, Guid clientId, Guid employeeUserId, AppointmentSlot slot, AppointmentStatus status, bool requestedByClient, Guid? actorUserId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot.CustomFields);

        if (Check(slot, now, checkFuture: true) is { Count: > 0 } errors)
        {
            return Errors.Scheduling.AppointmentInvalid(errors);
        }

        var created = new Appointment(id, clientId, employeeUserId, slot, status, requestedByClient);
        created.history.Add(new AppointmentStatusChange(Guid.CreateVersion7(), id, 1, null, status, now, actorUserId, null));
        return created;
    }

    private static Dictionary<string, string[]> Check(AppointmentSlot slot, DateTimeOffset now, bool checkFuture)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (checkFuture && slot.StartsAt <= now)
        {
            errors["startsAt"] = ["validation.appointments.future"];
        }

        if (slot.DurationMinutes is < MinDurationMinutes or > MaxDurationMinutes)
        {
            errors["durationMinutes"] = ["validation.appointments.duration"];
        }

        if (slot.Notes?.Trim().Length > NotesMaxLength)
        {
            errors["notes"] = ["validation.appointments.notes"];
        }

        return errors;
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private Result Move(AppointmentStatus to, Guid? actorUserId, string? note, DateTimeOffset now)
    {
        if (note?.Trim().Length > NoteMaxLength)
        {
            return Errors.Scheduling.AppointmentInvalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["note"] = ["validation.appointments.note"] });
        }

        history.Add(new AppointmentStatusChange(Guid.CreateVersion7(), Id, history.Count + 1, Status, to, now, actorUserId, Text(note)));
        Status = to;
        return Result.Success();
    }
}

/// <summary>A status change of an appointment (<c>scheduling.appointment_status_history</c>).</summary>
public sealed class AppointmentStatusChange : Entity<Guid>
{
    internal AppointmentStatusChange(
        Guid id, Guid appointmentId, int sequence, AppointmentStatus? fromStatus, AppointmentStatus toStatus, DateTimeOffset changedAt, Guid? changedByUserId, string? note)
        : base(id)
    {
        AppointmentId = appointmentId;
        Sequence = sequence;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ChangedAt = changedAt;
        ChangedByUserId = changedByUserId;
        Note = note;
    }

    private AppointmentStatusChange()
    {
    }

    public Guid AppointmentId { get; private set; }

    /// <summary>1 for the creation, then one more for each change (unique per appointment).</summary>
    public int Sequence { get; private set; }

    /// <summary><c>null</c> for the creation.</summary>
    public AppointmentStatus? FromStatus { get; private set; }

    public AppointmentStatus ToStatus { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public Guid? ChangedByUserId { get; private set; }

    public string? Note { get; private set; }
}
