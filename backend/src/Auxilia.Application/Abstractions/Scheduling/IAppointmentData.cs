using Auxilia.Domain.Scheduling;

namespace Auxilia.Application.Abstractions.Scheduling;

/// <summary>
/// Which appointments a caller lists (F13), applied in the query: everything, an employee's own, or the appointments of
/// one client. Nothing when none is set. (Employees also open the global calendar's appointments: <c>GlobalOnly</c>.)
/// </summary>
public sealed record AppointmentScope(bool Everything, Guid? EmployeeUserId, Guid? ClientId)
{
    public static readonly AppointmentScope None = new(false, null, null);
}

/// <summary>Sortable columns of the appointment grid (legacy: date and status; default date descending).</summary>
public enum AppointmentSort
{
    StartsAt,
    Status,
    Client,
    Employee,
}

/// <summary>
/// A page or range request, already validated: <see cref="From"/> (included) and <see cref="To"/> (excluded) are
/// instants. <see cref="GlobalOnly"/> keeps the shared calendar: flagged and still open, any employee.
/// </summary>
public sealed record AppointmentFilter(
    AppointmentScope Scope,
    Guid? ClientId,
    Guid? EmployeeUserId,
    AppointmentStatus? Status,
    DateTimeOffset? From,
    DateTimeOffset? To,
    bool GlobalOnly,
    AppointmentSort Sort,
    bool Descending,
    int Skip,
    int Take);

/// <summary>An appointment as the lists and calendars show it.</summary>
public sealed record AppointmentRow(
    Guid Id,
    Guid ClientId,
    string ClientName,
    Guid EmployeeUserId,
    string EmployeeName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int DurationMinutes,
    AppointmentStatus Status,
    bool ShowInGlobalCalendar,
    bool RequestedByClient,
    string CustomFields);

/// <summary>The people of an appointment: names (deleted people included) and the client's user, told of changes.</summary>
public sealed record AppointmentPeople(string ClientName, Guid? ClientUserId, string EmployeeName);

/// <summary>An active employee (can sign in) a client may ask for.</summary>
public sealed record AppointmentEmployee(Guid UserId, string FullName);

/// <summary>
/// Appointments of the current tenant (F13), one unit of work (inside a write operation it joins the operation's
/// transaction). Deleted appointments are never returned; neither are those of deleted clients in the lists.
/// </summary>
public interface IAppointmentData : IAsyncDisposable
{
    Task<(IReadOnlyList<AppointmentRow> Items, int Total)> PageAsync(AppointmentFilter filter, CancellationToken cancellationToken);

    /// <summary>The starts of the appointments of the filter (paging ignored, at most <paramref name="max"/>), for per-day charts in the tenant time zone.</summary>
    Task<IReadOnlyList<DateTimeOffset>> StartsAsync(AppointmentFilter filter, int max, CancellationToken cancellationToken);

    /// <summary>How many appointments of the filter are in each status (paging ignored).</summary>
    Task<IReadOnlyDictionary<AppointmentStatus, int>> CountByStatusAsync(AppointmentFilter filter, CancellationToken cancellationToken);

    /// <summary>The open appointments of the employee overlapping [<paramref name="startsAt"/>, <paramref name="endsAt"/>).</summary>
    Task<IReadOnlyList<AppointmentRow>> OverlappingAsync(
        Guid employeeUserId, DateTimeOffset startsAt, DateTimeOffset endsAt, Guid? excludeId, CancellationToken cancellationToken);

    /// <summary>The appointment with its history; tracked unless <paramref name="readOnly"/>.</summary>
    Task<Appointment?> FindAsync(Guid id, bool readOnly, CancellationToken cancellationToken);

    Task<AppointmentPeople> PeopleAsync(Guid clientId, Guid employeeUserId, CancellationToken cancellationToken);

    /// <summary>Names of the users (person first and last name), deleted people included.</summary>
    Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>The person of a user (a client's own appointments).</summary>
    Task<Guid?> PersonOfUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The users with the Employee role who can sign in and whose person is not deleted, by name.</summary>
    Task<IReadOnlyList<AppointmentEmployee>> ActiveEmployeesAsync(CancellationToken cancellationToken);

    void Add(Appointment appointment);

    /// <summary>Soft delete.</summary>
    void Remove(Appointment appointment);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IAppointmentDataFactory
{
    Task<IAppointmentData> OpenAsync(CancellationToken cancellationToken);
}
