using System.Globalization;
using System.Text.Json;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Realtime;
using Auxilia.Application.Abstractions.Scheduling;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Configuration.Public;
using Auxilia.Application.Directory.Public;
using Auxilia.Application.Engagement.Public;
using Auxilia.Application.Identity.Public;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Realtime;
using Auxilia.Contracts.Scheduling;
using Auxilia.Diagnostics;
using Auxilia.Domain.Scheduling;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Scheduling;

/// <summary>What is asked of an appointment the caller sees (F13).</summary>
public enum AppointmentAction
{
    /// <summary>Move, edit, approve, reject, complete.</summary>
    Manage,

    /// <summary>Cancel (Q21: the client too).</summary>
    Cancel,

    Delete,
}

/// <summary>An appointment as the access rules see it.</summary>
public sealed record AppointmentResource(Guid ClientId, Guid EmployeeUserId, bool ShowInGlobalCalendar, AppointmentStatus Status, AppointmentAction Action = AppointmentAction.Manage);

/// <summary>
/// Appointments of the current tenant (F13, Q19–Q22): staff schedule them already approved, clients request them;
/// staff move, approve, reject, complete and delete them, both sides cancel. Every change is told to the other party
/// (realtime <c>AppointmentChanged</c>; the persisted notifications come with B-19).
/// </summary>
public interface IAppointmentManager
{
    Task<Result<Guid>> ScheduleAsync(ScheduleAppointmentRequest request, CancellationToken cancellationToken);

    Task<Result<Guid>> RequestAsync(RequestAppointmentRequest request, CancellationToken cancellationToken);

    Task<Result> UpdateAsync(Guid id, UpdateAppointmentRequest request, CancellationToken cancellationToken);

    Task<Result> ApproveAsync(Guid id, string? note, CancellationToken cancellationToken);

    Task<Result> RejectAsync(Guid id, string? note, CancellationToken cancellationToken);

    Task<Result> CompleteAsync(Guid id, string? note, CancellationToken cancellationToken);

    Task<Result> CancelAsync(Guid id, string? note, CancellationToken cancellationToken);

    /// <summary>Soft delete by staff (history kept).</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IAppointmentQueryService
{
    Task<Result<PagedResponse<AppointmentListItemResponse>>> ListAsync(AppointmentListQuery query, CancellationToken cancellationToken);

    Task<Result<AppointmentCalendarResponse>> CalendarAsync(AppointmentCalendarQuery query, CancellationToken cancellationToken);

    /// <summary>The appointment with its timeline; <c>AUX-15010</c> when the caller cannot see it.</summary>
    Task<Result<AppointmentResponse>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Staff: the open appointments of the employee overlapping a slot (employees: their own only).</summary>
    Task<Result<IReadOnlyList<AppointmentConflictResponse>>> ConflictsAsync(AppointmentConflictQuery query, CancellationToken cancellationToken);

    /// <summary>Clients: the active employees to ask for (Q22), the one in charge marked.</summary>
    Task<Result<IReadOnlyList<AppointmentEmployeeChoiceResponse>>> EmployeesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// F13: Administrators see and manage everything. Employees see their own appointments and those on the global
/// calendar still open (read only), and manage their own. Clients see their own and may only cancel them (Q21).
/// </summary>
internal sealed class AppointmentAccessPolicy(ICurrentUser currentUser, IAppointmentDataFactory data) : IResourceAccessPolicy<AppointmentResource>
{
    private Guid? personId;
    private bool personLoaded;

    public bool IsAdministrator => Has(TenantRole.Administrator);

    public bool IsEmployee => Has(TenantRole.Employee);

    public bool IsStaff => IsAdministrator || IsEmployee;

    public bool IsClient => Has(TenantRole.Client);

    public async Task<bool> CanAccessAsync(AppointmentResource resource, string permission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return permission switch
        {
            SchedulingPermissions.ViewAppointments => await CanSeeAsync(resource, cancellationToken),
            SchedulingPermissions.ManageAppointments => resource.Action == AppointmentAction.Cancel
                ? await CanCancelAsync(resource, cancellationToken)
                : CanManage(resource),
            _ => false,
        };
    }

    /// <summary>The same visibility as <see cref="CanSeeAsync"/>, as a query filter (never post-filtering).</summary>
    public async Task<AppointmentScope> ScopeAsync(CancellationToken cancellationToken)
    {
        if (IsAdministrator)
        {
            return new AppointmentScope(true, null, null);
        }

        if (IsEmployee)
        {
            return new AppointmentScope(false, currentUser.UserId, null);
        }

        return IsClient && await PersonAsync(cancellationToken) is { } person ? new AppointmentScope(false, null, person) : AppointmentScope.None;
    }

    public async Task<bool> CanSeeAsync(AppointmentResource resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return IsAdministrator
            || (IsEmployee && (resource.EmployeeUserId == currentUser.UserId || (resource.ShowInGlobalCalendar && Appointment.IsOpen(resource.Status))))
            || (IsClient && await PersonAsync(cancellationToken) == resource.ClientId);
    }

    public bool CanManage(AppointmentResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return IsAdministrator || (IsEmployee && resource.EmployeeUserId == currentUser.UserId);
    }

    public async Task<bool> CanCancelAsync(AppointmentResource resource, CancellationToken cancellationToken) =>
        CanManage(resource) || (IsClient && await PersonAsync(cancellationToken) == resource.ClientId);

    /// <summary>The person of the calling user (clients).</summary>
    public async Task<Guid?> PersonAsync(CancellationToken cancellationToken)
    {
        if (!personLoaded)
        {
            if (currentUser.UserId is { } userId)
            {
                await using var store = await data.OpenAsync(cancellationToken);
                personId = await store.PersonOfUserAsync(userId, cancellationToken);
            }

            personLoaded = true;
        }

        return personId;
    }

    private bool Has(TenantRole role) => currentUser.ActorType == ActorType.User && currentUser.Roles.Contains(role);
}

/// <summary>Local days and times of the tenant (Q20: the past is judged on instants, the input is local).</summary>
internal static class TenantTime
{
    public static TimeZoneInfo Zone(ITenantContext tenant) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(tenant.Tenant.TimeZone, out var zone) ? zone : TimeZoneInfo.Utc;

    /// <summary>The instant of a local date and time; <c>null</c> for a time skipped by a daylight-saving change.</summary>
    public static DateTimeOffset? Instant(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        return zone.IsInvalidTime(local) ? null : new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    /// <summary>The start of a local day.</summary>
    public static DateTimeOffset StartOf(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    public static (DateOnly Date, TimeOnly Time) Local(DateTimeOffset instant, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        return (DateOnly.FromDateTime(local.DateTime), TimeOnly.FromDateTime(local.DateTime));
    }
}

internal sealed class AppointmentManager(
    IOperationRunner operations,
    IAppointmentDataFactory data,
    IClientDirectory clients,
    IUserAccounts accounts,
    ICustomFieldValidator customFields,
    IAccessGuard guard,
    AppointmentAccessPolicy policy,
    IRealtimeNotifier notifier,
    INotificationSender notifications,
    ITenantContext tenant,
    ICurrentUser currentUser,
    TimeProvider clock) : IAppointmentManager
{
    public const string CustomFieldEntity = "appointment";

    public Task<Result<Guid>> ScheduleAsync(ScheduleAppointmentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<Guid>(Operations.Scheduling.ScheduleAppointment, new { request.ClientId, request.EmployeeUserId }, async scope =>
        {
            var allowed = await guard.EnsureAsync(SchedulingPermissions.ManageAppointments, cancellationToken);
            if (allowed.IsFailure || !policy.IsStaff)
            {
                return Result.Failure<Guid>(allowed.Error ?? Errors.Identity.PermissionDenied());
            }

            var fields = await customFields.ValidateAsync(CustomFieldEntity, request.CustomFields, cancellationToken);
            if (fields.IsFailure)
            {
                return Result.Failure<Guid>(fields.Error!);
            }

            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var client = await clients.FindAsync(request.ClientId, cancellationToken);
            if (client is null)
            {
                errors["clientId"] = ["validation.appointments.client"];
            }

            // Employees schedule for themselves; Administrators for whoever they choose, else the employee in charge.
            var employee = policy.IsAdministrator ? request.EmployeeUserId ?? client?.EmployeeUserId : currentUser.UserId;
            if (client is not null && employee is null)
            {
                errors["employeeUserId"] = ["validation.appointments.employee"];
            }
            else if (employee is { } chosen && policy.IsAdministrator && !await IsActiveEmployeeAsync(chosen, cancellationToken))
            {
                errors["employeeUserId"] = ["validation.appointments.employee"];
            }

            var startsAt = Start(request.Date, request.Time, errors);
            if (errors.Count > 0)
            {
                return Errors.Scheduling.AppointmentInvalid(errors);
            }

            // Legacy: an employee picks among the clients in charge.
            if (!policy.IsAdministrator && client!.EmployeeUserId != currentUser.UserId)
            {
                return Errors.Scheduling.AppointmentClientNotInCharge();
            }

            var now = clock.GetUtcNow();
            var slot = new AppointmentSlot(startsAt!.Value, request.DurationMinutes ?? Appointment.DefaultDurationMinutes, request.Notes, request.ShowInGlobalCalendar ?? true, fields.Value);
            var scheduled = Appointment.Schedule(Guid.CreateVersion7(), client!.Id, employee!.Value, slot, currentUser.UserId, now);
            return await AddAsync(scope, scheduled, "Scheduled", cancellationToken);
        }, cancellationToken);
    }

    public Task<Result<Guid>> RequestAsync(RequestAppointmentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<Guid>(Operations.Scheduling.RequestAppointment, new { request.EmployeeUserId }, async scope =>
        {
            var allowed = await guard.EnsureAsync(SchedulingPermissions.ManageAppointments, cancellationToken);
            if (allowed.IsFailure || !policy.IsClient || await policy.PersonAsync(cancellationToken) is not { } person)
            {
                return Result.Failure<Guid>(allowed.Error ?? Errors.Identity.PermissionDenied());
            }

            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (!await IsActiveEmployeeAsync(request.EmployeeUserId, cancellationToken))
            {
                errors["employeeUserId"] = ["validation.appointments.employee"];
            }

            var startsAt = Start(request.Date, request.Time, errors);
            if (errors.Count > 0)
            {
                return Errors.Scheduling.AppointmentInvalid(errors);
            }

            // A client request carries no custom fields (staff fill them later).
            var slot = new AppointmentSlot(startsAt!.Value, request.DurationMinutes ?? Appointment.DefaultDurationMinutes, request.Notes, ShowInGlobalCalendar: true, "{}");
            var requested = Appointment.Request(Guid.CreateVersion7(), person, request.EmployeeUserId, slot, currentUser.UserId, clock.GetUtcNow());
            return await AddAsync(scope, requested, "Requested", cancellationToken);
        }, cancellationToken);
    }

    public Task<Result> UpdateAsync(Guid id, UpdateAppointmentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ChangeAsync(Operations.Scheduling.UpdateAppointment, id, AppointmentAction.Manage, "Updated", async (appointment, now) =>
        {
            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var startsAt = Start(request.Date, request.Time, errors);
            if (errors.Count > 0)
            {
                return Errors.Scheduling.AppointmentInvalid(errors);
            }

            var fields = await customFields.ValidateAsync(CustomFieldEntity, request.CustomFields, cancellationToken);
            return fields.IsFailure
                ? Result.Failure(fields.Error!)
                : appointment.Update(new AppointmentSlot(startsAt!.Value, request.DurationMinutes, request.Notes, request.ShowInGlobalCalendar, fields.Value), now);
        }, cancellationToken);
    }

    public Task<Result> ApproveAsync(Guid id, string? note, CancellationToken cancellationToken) =>
        ChangeAsync(Operations.Scheduling.ApproveAppointment, id, AppointmentAction.Manage, "Approved",
            (appointment, now) => Task.FromResult(appointment.Approve(currentUser.UserId, note, now)), cancellationToken);

    public Task<Result> RejectAsync(Guid id, string? note, CancellationToken cancellationToken) =>
        ChangeAsync(Operations.Scheduling.RejectAppointment, id, AppointmentAction.Manage, "Rejected",
            (appointment, now) => Task.FromResult(appointment.Reject(currentUser.UserId, note, now)), cancellationToken);

    public Task<Result> CompleteAsync(Guid id, string? note, CancellationToken cancellationToken) =>
        ChangeAsync(Operations.Scheduling.CompleteAppointment, id, AppointmentAction.Manage, "Completed",
            (appointment, now) => Task.FromResult(appointment.Complete(currentUser.UserId, note, now)), cancellationToken);

    public Task<Result> CancelAsync(Guid id, string? note, CancellationToken cancellationToken) =>
        ChangeAsync(Operations.Scheduling.CancelAppointment, id, AppointmentAction.Cancel, "Cancelled",
            (appointment, now) => Task.FromResult(appointment.Cancel(currentUser.UserId, note, now)), cancellationToken);

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Scheduling.DeleteAppointment, new { AppointmentId = id }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var loaded = await LoadAsync(store, id, AppointmentAction.Delete, cancellationToken);
            if (loaded.IsFailure)
            {
                return Result.Failure(loaded.Error!);
            }

            store.Remove(loaded.Value);
            await store.SaveChangesAsync(cancellationToken);
            await TellAsync(scope, store, loaded.Value, "Deleted", cancellationToken);
            return Result.Success();
        }, cancellationToken);

    private DateTimeOffset? Start(DateOnly date, TimeOnly time, Dictionary<string, string[]> errors)
    {
        var startsAt = TenantTime.Instant(date, time, TenantTime.Zone(tenant));
        if (startsAt is null)
        {
            errors["time"] = ["validation.appointments.time"];
        }

        return startsAt;
    }

    private async Task<bool> IsActiveEmployeeAsync(Guid userId, CancellationToken cancellationToken) =>
        (await accounts.FindManyAsync([userId], cancellationToken)).Any(account => account.CanSignIn && account.Roles.Contains(TenantRole.Employee));

    private async Task<Result<Guid>> AddAsync(IOperationScope scope, Result<Appointment> created, string change, CancellationToken cancellationToken)
    {
        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error!);
        }

        await using var store = await data.OpenAsync(cancellationToken);
        store.Add(created.Value);
        await store.SaveChangesAsync(cancellationToken);
        scope.SetEntity(nameof(Appointment), created.Value.Id);
        await TellAsync(scope, store, created.Value, change, cancellationToken);
        return created.Value.Id;
    }

    private Task<Result> ChangeAsync(
        OperationDescriptor operation, Guid id, AppointmentAction action, string change, Func<Appointment, DateTimeOffset, Task<Result>> apply, CancellationToken cancellationToken) =>
        operations.RunAsync(operation, new { AppointmentId = id }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var loaded = await LoadAsync(store, id, action, cancellationToken);
            if (loaded.IsFailure)
            {
                return Result.Failure(loaded.Error!);
            }

            var changed = await apply(loaded.Value, clock.GetUtcNow());
            if (changed.IsFailure)
            {
                return changed;
            }

            await store.SaveChangesAsync(cancellationToken);
            await TellAsync(scope, store, loaded.Value, change, cancellationToken);
            return Result.Success();
        }, cancellationToken);

    /// <summary>The appointment (tracked): 404 when the caller cannot see it, 403 when it can but may not do this.</summary>
    private async Task<Result<Appointment>> LoadAsync(IAppointmentData store, Guid id, AppointmentAction action, CancellationToken cancellationToken)
    {
        if (await store.FindAsync(id, readOnly: false, cancellationToken) is not { } appointment)
        {
            return Errors.Scheduling.AppointmentNotFound();
        }

        var resource = new AppointmentResource(appointment.ClientId, appointment.EmployeeUserId, appointment.ShowInGlobalCalendar, appointment.Status, action);
        if (!await policy.CanSeeAsync(resource, cancellationToken))
        {
            return Errors.Scheduling.AppointmentNotFound();
        }

        var allowed = await guard.EnsureAsync(SchedulingPermissions.ManageAppointments, resource, cancellationToken);
        return allowed.IsFailure ? Result.Failure<Appointment>(allowed.Error!) : appointment;
    }

    /// <summary>
    /// The other party (client's user and employee, never the actor) hears of the change: a notification (F16, in the
    /// same transaction) and, after the commit, the realtime <c>AppointmentChanged</c> that refreshes calendars.
    /// </summary>
    private async Task TellAsync(IOperationScope scope, IAppointmentData store, Appointment appointment, string change, CancellationToken cancellationToken)
    {
        var people = await store.PeopleAsync(appointment.ClientId, appointment.EmployeeUserId, cancellationToken);
        var pushed = new AppointmentChangedEvent(appointment.Id, change, appointment.StartsAt);
        var recipients = new[] { people.ClientUserId, appointment.EmployeeUserId }.OfType<Guid>().Distinct().Where(user => user != currentUser.UserId).ToArray();
        foreach (var recipient in recipients)
        {
            scope.OnCommitted(ct => notifier.ToUserAsync(recipient, RealtimeEvents.AppointmentChanged, pushed, ct));
        }

        var (date, time) = TenantTime.Local(appointment.StartsAt, TenantTime.Zone(tenant));
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["when"] = $"{date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} {time.ToString("HH:mm", CultureInfo.InvariantCulture)}",
            ["client"] = people.ClientName,
            ["employee"] = people.EmployeeName,
        };
        await notifications.NotifyUsersAsync(recipients, new NotificationMessage(Kinds[change], appointment.Id, parameters), cancellationToken);
    }

    private static readonly Dictionary<string, string> Kinds = new(StringComparer.Ordinal)
    {
        ["Scheduled"] = NotificationKinds.AppointmentScheduled,
        ["Requested"] = NotificationKinds.AppointmentRequested,
        ["Updated"] = NotificationKinds.AppointmentUpdated,
        ["Approved"] = NotificationKinds.AppointmentApproved,
        ["Rejected"] = NotificationKinds.AppointmentRejected,
        ["Completed"] = NotificationKinds.AppointmentCompleted,
        ["Cancelled"] = NotificationKinds.AppointmentCancelled,
        ["Deleted"] = NotificationKinds.AppointmentDeleted,
    };
}

internal sealed class AppointmentQueryService(
    IAppointmentDataFactory data,
    AppointmentAccessPolicy policy,
    IPermissionAccess permissions,
    IClientDirectory clients,
    ITenantContext tenant) : IAppointmentQueryService
{
    public const int MaxPageSize = 100;
    public const int MaxCalendarDays = 62;
    public const int MaxCalendarItems = 1000;

    private static readonly Dictionary<string, AppointmentSort> Sorts = new(StringComparer.Ordinal)
    {
        ["startsAt"] = AppointmentSort.StartsAt,
        ["status"] = AppointmentSort.Status,
        ["client"] = AppointmentSort.Client,
        ["employee"] = AppointmentSort.Employee,
    };

    public async Task<Result<PagedResponse<AppointmentListItemResponse>>> ListAsync(AppointmentListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (query.Page < 1)
        {
            errors["page"] = ["validation.paging.page"];
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = ["validation.paging.pageSize"];
        }

        var sortField = query.Sort?.TrimStart('-');
        var sort = AppointmentSort.StartsAt;
        if (!string.IsNullOrEmpty(sortField) && !Sorts.TryGetValue(sortField, out sort))
        {
            errors["sort"] = ["validation.paging.sort"];
        }

        var status = Status(query.Status, errors);
        if (query.From is { } from && query.To is { } to && to < from)
        {
            errors["to"] = ["validation.appointments.range"];
        }

        if (errors.Count > 0)
        {
            return Errors.Host.ValidationFailed(errors);
        }

        // Legacy: the latest first unless a sort is chosen.
        var descending = string.IsNullOrEmpty(query.Sort) || query.Sort.StartsWith('-');
        var zone = TenantTime.Zone(tenant);
        var filter = new AppointmentFilter(
            await policy.ScopeAsync(cancellationToken),
            query.ClientId,
            query.EmployeeUserId,
            status,
            query.From is { } start ? TenantTime.StartOf(start, zone) : null,
            query.To is { } end ? TenantTime.StartOf(end.AddDays(1), zone) : null,
            GlobalOnly: false,
            sort,
            descending,
            (query.Page - 1) * query.PageSize,
            query.PageSize);
        await using var store = await data.OpenAsync(cancellationToken);
        var (items, total) = await store.PageAsync(filter, cancellationToken);
        return new PagedResponse<AppointmentListItemResponse>(items.Select(row => ToResponse(row, zone)).ToArray(), query.Page, query.PageSize, total);
    }

    public async Task<Result<AppointmentCalendarResponse>> CalendarAsync(AppointmentCalendarQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.To < query.From || query.To.DayNumber - query.From.DayNumber >= MaxCalendarDays)
        {
            return Errors.Host.ValidationFailed(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["to"] = ["validation.appointments.range"] });
        }

        if (query.Global && !policy.IsStaff)
        {
            return Errors.Identity.PermissionDenied();
        }

        var zone = TenantTime.Zone(tenant);
        var scope = query.Global ? new AppointmentScope(true, null, null) : await policy.ScopeAsync(cancellationToken);
        var filter = new AppointmentFilter(
            scope,
            query.ClientId,
            query.EmployeeUserId,
            null,
            TenantTime.StartOf(query.From, zone),
            TenantTime.StartOf(query.To.AddDays(1), zone),
            GlobalOnly: query.Global,
            AppointmentSort.StartsAt,
            Descending: false,
            0,
            MaxCalendarItems);
        await using var store = await data.OpenAsync(cancellationToken);
        var (items, _) = await store.PageAsync(filter, cancellationToken);
        return new AppointmentCalendarResponse(zone.Id, items.Select(row => ToResponse(row, zone)).ToArray());
    }

    public async Task<Result<AppointmentResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        if (await store.FindAsync(id, readOnly: true, cancellationToken) is not { } appointment)
        {
            return Errors.Scheduling.AppointmentNotFound();
        }

        var resource = new AppointmentResource(appointment.ClientId, appointment.EmployeeUserId, appointment.ShowInGlobalCalendar, appointment.Status);
        if (!await policy.CanSeeAsync(resource, cancellationToken))
        {
            return Errors.Scheduling.AppointmentNotFound();
        }

        var people = await store.PeopleAsync(appointment.ClientId, appointment.EmployeeUserId, cancellationToken);
        var history = appointment.History;
        var users = await store.UserNamesAsync(history.Select(change => change.ChangedByUserId).OfType<Guid>().Distinct().ToArray(), cancellationToken);
        AppointmentUserResponse? User(Guid? userId) => userId is { } known ? new AppointmentUserResponse(known, users.GetValueOrDefault(known, string.Empty)) : null;

        var mayManage = await permissions.HasAsync(SchedulingPermissions.ManageAppointments, cancellationToken);
        var open = Appointment.IsOpen(appointment.Status);
        var canManage = mayManage && policy.CanManage(resource);
        var canCancel = mayManage && open && await policy.CanCancelAsync(resource, cancellationToken);
        var hasConflict = policy.IsStaff && open
            && (await store.OverlappingAsync(appointment.EmployeeUserId, appointment.StartsAt, appointment.EndsAt, appointment.Id, cancellationToken)).Count > 0;
        var zone = TenantTime.Zone(tenant);
        var (date, time) = TenantTime.Local(appointment.StartsAt, zone);
        using var customFields = JsonDocument.Parse(appointment.CustomFields);
        return new AppointmentResponse(
            appointment.Id,
            new AppointmentClientResponse(appointment.ClientId, people.ClientName),
            new AppointmentEmployeeResponse(appointment.EmployeeUserId, people.EmployeeName),
            appointment.StartsAt,
            appointment.EndsAt,
            date,
            time,
            appointment.DurationMinutes,
            appointment.Status.ToString(),
            appointment.Notes,
            appointment.ShowInGlobalCalendar,
            appointment.RequestedByClient,
            customFields.RootElement.Clone(),
            history.Select(change => new AppointmentStatusChangeResponse(change.FromStatus?.ToString(), change.ToStatus.ToString(), change.ChangedAt, User(change.ChangedByUserId), change.Note)).ToArray(),
            hasConflict,
            canManage && open,
            canCancel,
            canManage);
    }

    public async Task<Result<IReadOnlyList<AppointmentConflictResponse>>> ConflictsAsync(AppointmentConflictQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!policy.IsStaff || (!policy.IsAdministrator && query.EmployeeUserId != (await policy.ScopeAsync(cancellationToken)).EmployeeUserId))
        {
            return Errors.Identity.PermissionDenied();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var startsAt = TenantTime.Instant(query.Date, query.Time, TenantTime.Zone(tenant));
        if (startsAt is null)
        {
            errors["time"] = ["validation.appointments.time"];
        }

        if (query.DurationMinutes is < Appointment.MinDurationMinutes or > Appointment.MaxDurationMinutes)
        {
            errors["durationMinutes"] = ["validation.appointments.duration"];
        }

        if (errors.Count > 0)
        {
            return Errors.Host.ValidationFailed(errors);
        }

        await using var store = await data.OpenAsync(cancellationToken);
        var overlapping = await store.OverlappingAsync(query.EmployeeUserId, startsAt!.Value, startsAt.Value.AddMinutes(query.DurationMinutes), query.ExcludeId, cancellationToken);
        return overlapping.Select(row => new AppointmentConflictResponse(row.Id, row.ClientName, row.StartsAt, row.EndsAt, row.Status.ToString())).ToArray();
    }

    public async Task<Result<IReadOnlyList<AppointmentEmployeeChoiceResponse>>> EmployeesAsync(CancellationToken cancellationToken)
    {
        if (!policy.IsClient || await policy.PersonAsync(cancellationToken) is not { } person)
        {
            return Errors.Identity.PermissionDenied();
        }

        var inCharge = (await clients.FindAsync(person, cancellationToken))?.EmployeeUserId;
        await using var store = await data.OpenAsync(cancellationToken);
        var employees = await store.ActiveEmployeesAsync(cancellationToken);
        return employees.Select(employee => new AppointmentEmployeeChoiceResponse(employee.UserId, employee.FullName, employee.UserId == inCharge)).ToArray();
    }

    private static AppointmentStatus? Status(string? value, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (Enum.TryParse<AppointmentStatus>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        errors["status"] = ["validation.appointments.status"];
        return null;
    }

    private static AppointmentListItemResponse ToResponse(AppointmentRow row, TimeZoneInfo zone)
    {
        var (date, time) = TenantTime.Local(row.StartsAt, zone);
        using var customFields = JsonDocument.Parse(row.CustomFields);
        return new AppointmentListItemResponse(
            row.Id,
            new AppointmentClientResponse(row.ClientId, row.ClientName),
            new AppointmentEmployeeResponse(row.EmployeeUserId, row.EmployeeName),
            row.StartsAt,
            row.EndsAt,
            date,
            time,
            row.DurationMinutes,
            row.Status.ToString(),
            row.ShowInGlobalCalendar,
            row.RequestedByClient,
            customFields.RootElement.Clone());
    }
}
