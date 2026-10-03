using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Application.Abstractions.Scheduling;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Scheduling;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Scheduling;

internal sealed class AppointmentDataFactory(ITenantDbContextFactory databases) : IAppointmentDataFactory
{
    public async Task<IAppointmentData> OpenAsync(CancellationToken cancellationToken) => new AppointmentData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="IAppointmentData"/>
internal sealed class AppointmentData(ITenantDbContext db) : IAppointmentData
{
    public async Task<(IReadOnlyList<AppointmentRow> Items, int Total)> PageAsync(AppointmentFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var appointments = Rows();
        var scope = filter.Scope;
        if (scope.ClientId is { } clientId)
        {
            appointments = appointments.Where(row => row.Appointment.ClientId == clientId);
        }
        else if (scope.EmployeeUserId is { } employeeUserId)
        {
            appointments = appointments.Where(row => row.Appointment.EmployeeUserId == employeeUserId);
        }
        else if (!scope.Everything)
        {
            appointments = appointments.Where(_ => false);
        }

        if (filter.GlobalOnly)
        {
            appointments = appointments.Where(row => row.Appointment.ShowInGlobalCalendar
                && (row.Appointment.Status == AppointmentStatus.Pending || row.Appointment.Status == AppointmentStatus.Approved));
        }

        if (filter.ClientId is { } onlyClient)
        {
            appointments = appointments.Where(row => row.Appointment.ClientId == onlyClient);
        }

        if (filter.EmployeeUserId is { } onlyEmployee)
        {
            appointments = appointments.Where(row => row.Appointment.EmployeeUserId == onlyEmployee);
        }

        if (filter.Status is { } status)
        {
            appointments = appointments.Where(row => row.Appointment.Status == status);
        }

        // A range shows what overlaps it (an appointment across midnight belongs to both days).
        if (filter.From is { } from)
        {
            appointments = appointments.Where(row => row.Appointment.EndsAt > from);
        }

        if (filter.To is { } to)
        {
            appointments = appointments.Where(row => row.Appointment.StartsAt < to);
        }

        var total = await appointments.CountAsync(cancellationToken);
        var sorted = (filter.Sort, filter.Descending) switch
        {
            (AppointmentSort.Status, false) => appointments.OrderBy(row => row.Appointment.Status).ThenByDescending(row => row.Appointment.StartsAt),
            (AppointmentSort.Status, true) => appointments.OrderByDescending(row => row.Appointment.Status).ThenByDescending(row => row.Appointment.StartsAt),
            (AppointmentSort.Client, false) => appointments.OrderBy(row => row.Client.LastName).ThenBy(row => row.Client.FirstName),
            (AppointmentSort.Client, true) => appointments.OrderByDescending(row => row.Client.LastName).ThenByDescending(row => row.Client.FirstName),
            (AppointmentSort.Employee, false) => appointments.OrderBy(row => row.Employee.LastName).ThenBy(row => row.Employee.FirstName),
            (AppointmentSort.Employee, true) => appointments.OrderByDescending(row => row.Employee.LastName).ThenByDescending(row => row.Employee.FirstName),
            (_, false) => appointments.OrderBy(row => row.Appointment.StartsAt),
            (_, true) => appointments.OrderByDescending(row => row.Appointment.StartsAt),
        };

        var items = await Project(sorted.ThenBy(row => row.Appointment.Id).Skip(filter.Skip).Take(filter.Take)).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<IReadOnlyList<AppointmentRow>> OverlappingAsync(
        Guid employeeUserId, DateTimeOffset startsAt, DateTimeOffset endsAt, Guid? excludeId, CancellationToken cancellationToken) =>
        await Project(Rows()
                .Where(row => row.Appointment.EmployeeUserId == employeeUserId
                    && (row.Appointment.Status == AppointmentStatus.Pending || row.Appointment.Status == AppointmentStatus.Approved)
                    && row.Appointment.StartsAt < endsAt
                    && row.Appointment.EndsAt > startsAt
                    && row.Appointment.Id != excludeId)
                .OrderBy(row => row.Appointment.StartsAt))
            .ToListAsync(cancellationToken);

    public Task<Appointment?> FindAsync(Guid id, bool readOnly, CancellationToken cancellationToken)
    {
        var appointments = readOnly ? db.Set<Appointment>().AsNoTracking() : db.Set<Appointment>();
        return appointments.SingleOrDefaultAsync(appointment => appointment.Id == id, cancellationToken);
    }

    /// <summary>Single-row lookups with <c>IgnoreQueryFilters</c> on their own: a deleted person keeps its name.</summary>
    public async Task<AppointmentPeople> PeopleAsync(Guid clientId, Guid employeeUserId, CancellationToken cancellationToken)
    {
        var client = await db.Set<Person>().IgnoreQueryFilters().AsNoTracking()
            .Where(person => person.Id == clientId)
            .Select(person => person.FirstName + " " + person.LastName)
            .SingleOrDefaultAsync(cancellationToken);
        var clientUserId = await db.Set<User>().AsNoTracking()
            .Where(user => user.PersonId == clientId)
            .Select(user => (Guid?)user.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var employee = (await UserNamesAsync([employeeUserId], cancellationToken)).GetValueOrDefault(employeeUserId, string.Empty);
        return new AppointmentPeople(client ?? string.Empty, clientUserId, employee);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await (
                    from user in db.Set<User>()
                    where userIds.Contains(user.Id)
                    join person in db.Set<Person>().IgnoreQueryFilters() on user.PersonId equals person.Id
                    select new { user.Id, Name = person.FirstName + " " + person.LastName })
                .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);

    public Task<Guid?> PersonOfUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<User>().AsNoTracking().Where(user => user.Id == userId).Select(user => (Guid?)user.PersonId).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<AppointmentEmployee>> ActiveEmployeesAsync(CancellationToken cancellationToken) =>
        await (
                from user in db.Set<User>().AsNoTracking()
                where user.IsActive && EF.Property<List<UserRole>>(user, "roles").Any(item => item.Role == TenantRole.Employee)
                join person in db.Set<Person>() on user.PersonId equals person.Id
                orderby person.LastName, person.FirstName, user.Id
                select new AppointmentEmployee(user.Id, person.FirstName + " " + person.LastName))
            .ToListAsync(cancellationToken);

    public void Add(Appointment appointment) => db.Set<Appointment>().Add(appointment);

    public void Remove(Appointment appointment) => db.Set<Appointment>().Remove(appointment);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();

    /// <summary>
    /// Appointments with the client's person and the employee's person, joined (not left-joined) so the soft-delete
    /// filters apply: no deleted appointment, no deleted client; the employee's person is read whatever its state.
    /// </summary>
    private IQueryable<AppointmentJoin> Rows() =>
        from appointment in db.Set<Appointment>().AsNoTracking()
        join client in db.Set<Person>() on appointment.ClientId equals client.Id
        join user in db.Set<User>() on appointment.EmployeeUserId equals user.Id
        join employee in db.Set<Person>().IgnoreQueryFilters() on user.PersonId equals employee.Id
        select new AppointmentJoin { Appointment = appointment, Client = client, Employee = employee };

    private static IQueryable<AppointmentRow> Project(IQueryable<AppointmentJoin> rows) =>
        rows.Select(row => new AppointmentRow(
            row.Appointment.Id,
            row.Appointment.ClientId,
            row.Client.FirstName + " " + row.Client.LastName,
            row.Appointment.EmployeeUserId,
            row.Employee.FirstName + " " + row.Employee.LastName,
            row.Appointment.StartsAt,
            row.Appointment.EndsAt,
            row.Appointment.DurationMinutes,
            row.Appointment.Status,
            row.Appointment.ShowInGlobalCalendar,
            row.Appointment.RequestedByClient,
            row.Appointment.CustomFields));

    private sealed class AppointmentJoin
    {
        public Appointment Appointment { get; init; } = null!;

        public Person Client { get; init; } = null!;

        public Person Employee { get; init; } = null!;
    }
}
