using System.Text.Json;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Scheduling;
using Auxilia.Application.Configuration.Public;
using Auxilia.Application.Directory.Public;
using Auxilia.Application.Identity.Public;
using Auxilia.Application.Scheduling;
using Auxilia.Application.Tests.Engagement;
using Auxilia.Application.Tests.Identity;
using Auxilia.Application.Tests.Platform;
using Auxilia.Contracts.Realtime;
using Auxilia.Contracts.Scheduling;
using Auxilia.Diagnostics;
using Auxilia.Domain.Scheduling;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using NSubstitute;

namespace Auxilia.Application.Tests.Scheduling;

public sealed class AppointmentManagerTests : IAsyncDisposable
{
    private static readonly Guid Admin = Guid.CreateVersion7();
    private static readonly Guid Employee = Guid.CreateVersion7();
    private static readonly Guid OtherEmployee = Guid.CreateVersion7();
    private static readonly Guid ClientUser = Guid.CreateVersion7();
    private static readonly Guid Client = Guid.CreateVersion7();
    private static readonly Guid OtherClient = Guid.CreateVersion7();

    // ManualTimeProvider starts on 2026-09-30 08:00 UTC = 10:00 in Rome (tenant time zone).
    private static readonly DateOnly Tomorrow = new(2026, 10, 1);

    private readonly InMemoryAppointments data = new();
    private readonly IClientDirectory clients = Substitute.For<IClientDirectory>();
    private readonly IUserAccounts accounts = Substitute.For<IUserAccounts>();
    private readonly ICustomFieldValidator customFields = Substitute.For<ICustomFieldValidator>();
    private readonly IAccessGuard guard = Substitute.For<IAccessGuard>();
    private readonly IPermissionAccess permissions = Substitute.For<IPermissionAccess>();
    private readonly ICurrentUser caller = Substitute.For<ICurrentUser>();
    private readonly RecordingRealtimeNotifier notifier = new();
    private readonly RecordingNotificationSender notifications = new();
    private readonly ManualTimeProvider clock = new();
    private readonly AppointmentAccessPolicy policy;
    private readonly AppointmentManager manager;
    private readonly AppointmentQueryService query;

    public AppointmentManagerTests()
    {
        CallAs(Admin, TenantRole.Administrator);
        data.Persons[ClientUser] = Client;
        data.ClientUsers[Client] = ClientUser;
        data.Employees.AddRange([new AppointmentEmployee(Employee, "Paola Neri"), new AppointmentEmployee(OtherEmployee, "Luca Blu")]);

        clients.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<Guid>() switch
        {
            var id when id == Client => new ClientSummary(id, "Mario Rossi", Employee),
            var id when id == OtherClient => new ClientSummary(id, "Anna Verdi", null),
            _ => null,
        });
        accounts.FindManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(call =>
            call.Arg<IReadOnlyCollection<Guid>>()
                .Where(id => id == Employee || id == OtherEmployee)
                .Select(id => new UserAccount(id, Guid.CreateVersion7(), "e", null, true, true, [TenantRole.Employee]))
                .ToArray());
        customFields.ValidateAsync("appointment", Arg.Any<JsonElement?>(), Arg.Any<CancellationToken>()).Returns(Result.Success("{\"room\":\"A\"}"));
        permissions.HasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        guard.EnsureAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success());

        policy = new AppointmentAccessPolicy(caller, data);
        guard.EnsureAsync(Arg.Any<string>(), Arg.Any<AppointmentResource>(), Arg.Any<CancellationToken>()).Returns(async call =>
            await policy.CanAccessAsync(call.ArgAt<AppointmentResource>(1), call.ArgAt<string>(0), call.ArgAt<CancellationToken>(2))
                ? Result.Success()
                : Result.Failure(Errors.Identity.PermissionDenied()));
        var tenant = SessionSettings.Tenant();
        manager = new AppointmentManager(ManagerHarness.Runner(), data, clients, accounts, customFields, guard, policy, notifier, notifications, tenant, caller, clock);
        query = new AppointmentQueryService(data, policy, permissions, clients, tenant);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => data.DisposeAsync();

    private void CallAs(Guid userId, params TenantRole[] roles)
    {
        caller.ActorType.Returns(ActorType.User);
        caller.UserId.Returns(userId);
        caller.Roles.Returns(roles);
    }

    private static ScheduleAppointmentRequest Schedule(Guid clientId, Guid? employee = null, DateOnly? date = null, TimeOnly? time = null) =>
        new(clientId, employee, date ?? Tomorrow, time ?? new TimeOnly(9, 0), null, " Notes ", null, null);

    private async Task<Guid> ScheduledAsync(Guid? employee = null) => (await manager.ScheduleAsync(Schedule(Client, employee), Ct)).Value;

    private async Task<Guid> RequestedAsync()
    {
        CallAs(ClientUser, TenantRole.Client);
        var id = (await manager.RequestAsync(new RequestAppointmentRequest(Employee, Tomorrow, new TimeOnly(15, 30), 45, null), Ct)).Value;
        CallAs(Admin, TenantRole.Administrator);
        return id;
    }

    private Appointment Stored(Guid id) => data.Appointments.Single(appointment => appointment.Id == id);

    [Fact]
    public async Task Schedule_ByAdministrator_IsApproved_InTheTenantTimeZone_ForTheEmployeeInCharge()
    {
        var id = await ScheduledAsync();

        var stored = Stored(id);
        (stored.Status, stored.EmployeeUserId, stored.DurationMinutes, stored.Notes, stored.ShowInGlobalCalendar, stored.CustomFields)
            .ShouldBe((AppointmentStatus.Approved, Employee, 60, "Notes", true, "{\"room\":\"A\"}"));
        // 09:00 in Rome on 1 October (CEST, UTC+2).
        stored.StartsAt.ShouldBe(new DateTimeOffset(2026, 10, 1, 7, 0, 0, TimeSpan.Zero));
        // The client is told; the actor (an Administrator) is not the employee, so the employee is told too.
        notifier.Pushes.Select(push => push.Target).ShouldBe([$"user:{ClientUser}", $"user:{Employee}"], ignoreOrder: true);
        notifier.Pushes.ShouldAllBe(push => push.EventName == RealtimeEvents.AppointmentChanged && ((AppointmentChangedEvent)push.Payload).Change == "Scheduled");
        notifications.Sent.Select(sent => sent.Target).ShouldBe([$"user:{ClientUser}", $"user:{Employee}"], ignoreOrder: true);
        var message = notifications.Sent[0].Message;
        (message.Kind, message.EntityId, message.Parameters["when"], message.Parameters["client"]).ShouldBe(
            ("appointment.scheduled", (Guid?)id, "01/10/2026 09:00", "Mario Rossi"));
    }

    [Fact]
    public async Task Schedule_RefusesThePast_UnknownClients_AndClientsWithoutAnEmployee()
    {
        // 09:00 today in Rome is already past (it is 10:00).
        var past = await manager.ScheduleAsync(Schedule(Client, date: new DateOnly(2026, 9, 30)), Ct);
        past.Error!.ValidationErrors.Keys.ShouldBe(["startsAt"]);

        var unknown = await manager.ScheduleAsync(Schedule(Guid.CreateVersion7()), Ct);
        unknown.Error!.ValidationErrors.Keys.ShouldBe(["clientId"]);

        var nobody = await manager.ScheduleAsync(Schedule(OtherClient), Ct);
        nobody.Error!.ValidationErrors.Keys.ShouldBe(["employeeUserId"]);

        var inactive = await manager.ScheduleAsync(Schedule(OtherClient, Guid.CreateVersion7()), Ct);
        inactive.Error!.ValidationErrors.Keys.ShouldBe(["employeeUserId"]);

        // 02:30 does not exist in Rome on 28 March 2027 (clocks go forward).
        var skipped = await manager.ScheduleAsync(Schedule(Client, date: new DateOnly(2027, 3, 28), time: new TimeOnly(2, 30)), Ct);
        skipped.Error!.ValidationErrors.Keys.ShouldBe(["time"]);
        data.Appointments.ShouldBeEmpty();
    }

    [Fact]
    public async Task Schedule_ByEmployee_OnlyForTheClientsInCharge_AndAlwaysForThemselves()
    {
        CallAs(Employee, TenantRole.Employee);

        var id = (await manager.ScheduleAsync(Schedule(Client, OtherEmployee), Ct)).Value;
        Stored(id).EmployeeUserId.ShouldBe(Employee);
        notifier.Pushes.Select(push => push.Target).ShouldBe([$"user:{ClientUser}"]);

        (await manager.ScheduleAsync(Schedule(OtherClient), Ct)).Error!.Code.ShouldBe(EventCodes.Scheduling.AppointmentClientNotInCharge);

        CallAs(ClientUser, TenantRole.Client);
        (await manager.ScheduleAsync(Schedule(Client), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);
    }

    [Fact]
    public async Task Request_ByClient_IsPending_WithoutCustomFields_AndTellsTheEmployee()
    {
        var id = await RequestedAsync();

        var stored = Stored(id);
        (stored.Status, stored.ClientId, stored.EmployeeUserId, stored.DurationMinutes, stored.RequestedByClient, stored.CustomFields)
            .ShouldBe((AppointmentStatus.Pending, Client, Employee, 45, true, "{}"));
        notifier.Pushes.ShouldHaveSingleItem().Target.ShouldBe($"user:{Employee}");

        CallAs(ClientUser, TenantRole.Client);
        var unknown = await manager.RequestAsync(new RequestAppointmentRequest(Guid.CreateVersion7(), Tomorrow, new TimeOnly(9, 0), null, null), Ct);
        unknown.Error!.ValidationErrors.Keys.ShouldBe(["employeeUserId"]);

        CallAs(Employee, TenantRole.Employee);
        (await manager.RequestAsync(new RequestAppointmentRequest(Employee, Tomorrow, new TimeOnly(9, 0), null, null), Ct))
            .Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);
    }

    [Fact]
    public async Task StatusChanges_FollowTheRules_AndTellTheOtherParty()
    {
        var id = await RequestedAsync();
        notifier.Pushes.Clear();

        CallAs(Employee, TenantRole.Employee);
        (await manager.ApproveAsync(id, "see you", Ct)).IsSuccess.ShouldBeTrue();
        notifier.Pushes.ShouldHaveSingleItem().Target.ShouldBe($"user:{ClientUser}");
        (await manager.RejectAsync(id, null, Ct)).Error!.Code.ShouldBe(EventCodes.Scheduling.AppointmentNotPending);
        (await manager.CompleteAsync(id, null, Ct)).IsSuccess.ShouldBeTrue();
        (await manager.CancelAsync(id, null, Ct)).Error!.Code.ShouldBe(EventCodes.Scheduling.AppointmentClosed);

        Stored(id).History.Select(change => change.ToStatus).ShouldBe([AppointmentStatus.Pending, AppointmentStatus.Approved, AppointmentStatus.Completed]);
    }

    [Fact]
    public async Task Clients_CancelTheirOwn_ButDoNotManageThem()
    {
        var id = await ScheduledAsync();
        notifier.Pushes.Clear();

        CallAs(ClientUser, TenantRole.Client);
        (await manager.ApproveAsync(id, null, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);
        (await manager.UpdateAsync(id, new UpdateAppointmentRequest(Tomorrow, new TimeOnly(11, 0), 60, null, true, null), Ct))
            .Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);
        (await manager.DeleteAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);
        (await manager.CancelAsync(id, "ill", Ct)).IsSuccess.ShouldBeTrue();

        Stored(id).Status.ShouldBe(AppointmentStatus.Cancelled);
        notifier.Pushes.ShouldHaveSingleItem().Target.ShouldBe($"user:{Employee}");
    }

    [Fact]
    public async Task OtherPeoplesAppointments_AreNotFound()
    {
        var id = await ScheduledAsync(OtherEmployee);
        Stored(id).Update(new AppointmentSlot(Stored(id).StartsAt, 60, null, false, "{}"), clock.GetUtcNow());

        CallAs(Employee, TenantRole.Employee);
        (await manager.CancelAsync(id, null, Ct)).Error!.Code.ShouldBe(EventCodes.Scheduling.AppointmentNotFound);

        // On the global calendar the employee sees it, but still does not manage it.
        Stored(id).Update(new AppointmentSlot(Stored(id).StartsAt, 60, null, true, "{}"), clock.GetUtcNow());
        var shared = (await query.GetAsync(id, Ct)).Value;
        (shared.CanManage, shared.CanCancel, shared.CanDelete).ShouldBe((false, false, false));
        (await manager.CompleteAsync(id, null, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);

        var other = Guid.CreateVersion7();
        data.Persons[other] = OtherClient;
        CallAs(other, TenantRole.Client);
        (await query.GetAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Scheduling.AppointmentNotFound);
    }

    [Fact]
    public async Task Update_MovesTheAppointment_ValidatesCustomFields_AndDelete_IsSoft()
    {
        var id = await ScheduledAsync();

        (await manager.UpdateAsync(id, new UpdateAppointmentRequest(Tomorrow, new TimeOnly(16, 15), 30, "moved", false, null), Ct)).IsSuccess.ShouldBeTrue();
        var stored = Stored(id);
        (stored.StartsAt, stored.DurationMinutes, stored.Notes, stored.ShowInGlobalCalendar)
            .ShouldBe((new DateTimeOffset(2026, 10, 1, 14, 15, 0, TimeSpan.Zero), 30, "moved", false));

        customFields.ValidateAsync("appointment", Arg.Any<JsonElement?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<string>(Errors.Configuration.CustomFieldValuesInvalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["customFields.room"] = ["x"] })));
        (await manager.UpdateAsync(id, new UpdateAppointmentRequest(Tomorrow, new TimeOnly(16, 15), 30, null, true, null), Ct))
            .Error!.ValidationErrors.Keys.ShouldBe(["customFields.room"]);

        notifier.Pushes.Clear();
        (await manager.DeleteAsync(id, Ct)).IsSuccess.ShouldBeTrue();
        data.Deleted.ShouldContain(id);
        ((AppointmentChangedEvent)notifier.Pushes[0].Payload).Change.ShouldBe("Deleted");
        (await query.GetAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Scheduling.AppointmentNotFound);
    }

    [Fact]
    public async Task Detail_ShowsLocalTime_History_Conflicts_AndWhatTheCallerMayDo()
    {
        var first = await ScheduledAsync();
        var second = (await manager.ScheduleAsync(Schedule(Client, date: Tomorrow, time: new TimeOnly(9, 30)), Ct)).Value;
        var later = (await manager.ScheduleAsync(Schedule(Client, date: Tomorrow, time: new TimeOnly(11, 0)), Ct)).Value;

        var detail = (await query.GetAsync(second, Ct)).Value;
        (detail.Date, detail.Time, detail.Status, detail.HasConflict, detail.CanManage, detail.CanCancel, detail.CanDelete)
            .ShouldBe((Tomorrow, new TimeOnly(9, 30), "Approved", true, true, true, true));
        detail.History.ShouldHaveSingleItem().ChangedBy!.UserId.ShouldBe(Admin);
        (await query.GetAsync(later, Ct)).Value.HasConflict.ShouldBeFalse();

        // 09:00–10:00 without the first one meets the second (09:30); 10:30–11:00 touches nothing (end excluded).
        var conflicts = (await query.ConflictsAsync(new AppointmentConflictQuery(Employee, Tomorrow, new TimeOnly(9, 0), 60, first), Ct)).Value;
        (await query.ConflictsAsync(new AppointmentConflictQuery(Employee, Tomorrow, new TimeOnly(10, 30), 30, null), Ct)).Value.ShouldBeEmpty();
        conflicts.ShouldHaveSingleItem().Id.ShouldBe(second);

        CallAs(ClientUser, TenantRole.Client);
        var mine = (await query.GetAsync(second, Ct)).Value;
        (mine.HasConflict, mine.CanManage, mine.CanCancel, mine.CanDelete).ShouldBe((false, false, true, false));
        (await query.ConflictsAsync(new AppointmentConflictQuery(Employee, Tomorrow, new TimeOnly(8, 30), 60, null), Ct))
            .Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);
    }

    [Fact]
    public async Task Conflicts_ByEmployee_OnlyForThemselves()
    {
        CallAs(Employee, TenantRole.Employee);

        (await query.ConflictsAsync(new AppointmentConflictQuery(Employee, Tomorrow, new TimeOnly(9, 0), 60, null), Ct)).IsSuccess.ShouldBeTrue();
        (await query.ConflictsAsync(new AppointmentConflictQuery(OtherEmployee, Tomorrow, new TimeOnly(9, 0), 60, null), Ct))
            .Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);
        (await query.ConflictsAsync(new AppointmentConflictQuery(Employee, Tomorrow, new TimeOnly(9, 0), 2, null), Ct))
            .Error!.ValidationErrors.Keys.ShouldBe(["durationMinutes"]);
    }

    [Fact]
    public async Task Lists_ApplyTheScope_LocalDays_AndValidateTheQuery()
    {
        await ScheduledAsync();

        (await query.ListAsync(new AppointmentListQuery(null, null, null, Tomorrow, Tomorrow, null, 1, 25), Ct)).Value.Items.ShouldHaveSingleItem();
        data.LastFilter!.Scope.Everything.ShouldBeTrue();
        // 1 October in Rome: from 30 September 22:00 UTC to 1 October 22:00 UTC; latest first by default.
        (data.LastFilter.From, data.LastFilter.To, data.LastFilter.Sort, data.LastFilter.Descending).ShouldBe(
            (new DateTimeOffset(2026, 9, 30, 22, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 1, 22, 0, 0, TimeSpan.Zero), AppointmentSort.StartsAt, true));

        var invalid = await query.ListAsync(new AppointmentListQuery(null, null, "Waiting", Tomorrow, Tomorrow.AddDays(-1), "notes", 0, 101), Ct);
        invalid.Error!.ValidationErrors.Keys.ShouldBe(["page", "pageSize", "sort", "status", "to"], ignoreOrder: true);

        CallAs(Employee, TenantRole.Employee);
        await query.ListAsync(new AppointmentListQuery(null, null, "Pending", null, null, "status", 1, 25), Ct);
        (data.LastFilter.Scope.EmployeeUserId, data.LastFilter.Status, data.LastFilter.Sort, data.LastFilter.Descending)
            .ShouldBe(((Guid?)Employee, (AppointmentStatus?)AppointmentStatus.Pending, AppointmentSort.Status, false));

        CallAs(ClientUser, TenantRole.Client);
        await query.ListAsync(new AppointmentListQuery(null, null, null, null, null, null, 1, 25), Ct);
        data.LastFilter.Scope.ClientId.ShouldBe(Client);

        // A client user without a person (a new scope, as in a new request) lists nothing.
        CallAs(Guid.CreateVersion7(), TenantRole.Client);
        var fresh = new AppointmentQueryService(data, new AppointmentAccessPolicy(caller, data), permissions, clients, SessionSettings.Tenant());
        await fresh.ListAsync(new AppointmentListQuery(null, null, null, null, null, null, 1, 25), Ct);
        data.LastFilter.Scope.ShouldBe(AppointmentScope.None);
    }

    [Fact]
    public async Task Calendar_LimitsTheRange_AndTheGlobalViewIsForStaff()
    {
        await ScheduledAsync();

        var calendar = (await query.CalendarAsync(new AppointmentCalendarQuery(Tomorrow, Tomorrow.AddDays(61), false, null, null), Ct)).Value;
        (calendar.TimeZone, calendar.Items.Count).ShouldBe(("Europe/Rome", 1));
        (await query.CalendarAsync(new AppointmentCalendarQuery(Tomorrow, Tomorrow.AddDays(62), false, null, null), Ct))
            .Error!.ValidationErrors.Keys.ShouldBe(["to"]);

        CallAs(Employee, TenantRole.Employee);
        await query.CalendarAsync(new AppointmentCalendarQuery(Tomorrow, Tomorrow, true, null, null), Ct);
        (data.LastFilter!.Scope.Everything, data.LastFilter.GlobalOnly, data.LastFilter.Descending).ShouldBe((true, true, false));

        CallAs(ClientUser, TenantRole.Client);
        (await query.CalendarAsync(new AppointmentCalendarQuery(Tomorrow, Tomorrow, true, null, null), Ct))
            .Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);
    }

    [Fact]
    public async Task Employees_ForClients_MarkTheOneInCharge()
    {
        CallAs(ClientUser, TenantRole.Client);

        var employees = (await query.EmployeesAsync(Ct)).Value;
        employees.Select(employee => (employee.UserId, employee.InCharge)).ShouldBe([(Employee, true), (OtherEmployee, false)]);

        CallAs(Employee, TenantRole.Employee);
        (await query.EmployeesAsync(Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);
    }
}

internal sealed class InMemoryAppointments : IAppointmentDataFactory, IAppointmentData
{
    public List<Appointment> Appointments { get; } = [];

    public HashSet<Guid> Deleted { get; } = [];

    /// <summary>User → person.</summary>
    public Dictionary<Guid, Guid> Persons { get; } = [];

    /// <summary>Client person → user.</summary>
    public Dictionary<Guid, Guid> ClientUsers { get; } = [];

    public List<AppointmentEmployee> Employees { get; } = [];

    public AppointmentFilter? LastFilter { get; private set; }

    public Task<IAppointmentData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<IAppointmentData>(this);

    /// <summary>Applies no filter (the SQL is tested on PostgreSQL); records what was asked.</summary>
    public Task<(IReadOnlyList<AppointmentRow> Items, int Total)> PageAsync(AppointmentFilter filter, CancellationToken cancellationToken)
    {
        LastFilter = filter;
        var rows = Live().Select(Row).ToArray();
        return Task.FromResult<(IReadOnlyList<AppointmentRow>, int)>((rows, rows.Length));
    }

    public Task<IReadOnlyList<DateTimeOffset>> StartsAsync(AppointmentFilter filter, int max, CancellationToken cancellationToken)
    {
        LastFilter = filter;
        return Task.FromResult<IReadOnlyList<DateTimeOffset>>(Live().Select(item => item.StartsAt).Take(max).ToArray());
    }

    public Task<IReadOnlyDictionary<AppointmentStatus, int>> CountByStatusAsync(AppointmentFilter filter, CancellationToken cancellationToken)
    {
        LastFilter = filter;
        return Task.FromResult<IReadOnlyDictionary<AppointmentStatus, int>>(Live().GroupBy(item => item.Status).ToDictionary(group => group.Key, group => group.Count()));
    }

    public Task<IReadOnlyList<AppointmentRow>> OverlappingAsync(
        Guid employeeUserId, DateTimeOffset startsAt, DateTimeOffset endsAt, Guid? excludeId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AppointmentRow>>(Live()
            .Where(item => item.EmployeeUserId == employeeUserId && Appointment.IsOpen(item.Status) && item.StartsAt < endsAt && item.EndsAt > startsAt && item.Id != excludeId)
            .Select(Row)
            .ToArray());

    public Task<Appointment?> FindAsync(Guid id, bool readOnly, CancellationToken cancellationToken) =>
        Task.FromResult(Live().SingleOrDefault(appointment => appointment.Id == id));

    public Task<AppointmentPeople> PeopleAsync(Guid clientId, Guid employeeUserId, CancellationToken cancellationToken) =>
        Task.FromResult(new AppointmentPeople("Mario Rossi", ClientUsers.TryGetValue(clientId, out var user) ? user : null, "Paola Neri"));

    public Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(userIds.ToDictionary(id => id, id => "User " + id.ToString("N")[..4]));

    public Task<Guid?> PersonOfUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Persons.TryGetValue(userId, out var person) ? (Guid?)person : null);

    public Task<IReadOnlyList<AppointmentEmployee>> ActiveEmployeesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AppointmentEmployee>>(Employees);

    public void Add(Appointment appointment) => Appointments.Add(appointment);

    public void Remove(Appointment appointment) => Deleted.Add(appointment.Id);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private IEnumerable<Appointment> Live() => Appointments.Where(appointment => !Deleted.Contains(appointment.Id));

    private static AppointmentRow Row(Appointment item) =>
        new(item.Id, item.ClientId, "Mario Rossi", item.EmployeeUserId, "Paola Neri", item.StartsAt, item.EndsAt, item.DurationMinutes, item.Status,
            item.ShowInGlobalCalendar, item.RequestedByClient, item.CustomFields);
}
