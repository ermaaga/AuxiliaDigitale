using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Engagement;
using Auxilia.Application.Abstractions.Timeline;
using Auxilia.Application.Cases.Public;
using Auxilia.Application.Directory.Public;
using Auxilia.Application.Engagement;
using Auxilia.Application.Engagement.Public;
using Auxilia.Application.Tests.Identity;
using Auxilia.Application.Tests.Platform;
using Auxilia.Contracts.Engagement;
using Auxilia.Diagnostics;
using Auxilia.Domain.Engagement;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using NSubstitute;

namespace Auxilia.Application.Tests.Engagement;

public sealed class TaskTests : IAsyncDisposable
{
    private static readonly Guid Admin = Guid.CreateVersion7();
    private static readonly Guid Employee = Guid.CreateVersion7();
    private static readonly Guid OtherEmployee = Guid.CreateVersion7();
    private static readonly Guid Client = Guid.CreateVersion7();
    private static readonly Guid ClientCase = Guid.CreateVersion7();
    private static readonly DateOnly Today = new(2026, 9, 30);

    private readonly InMemoryTasks data = new();
    private readonly IClientDirectory clients = Substitute.For<IClientDirectory>();
    private readonly ICaseDirectory cases = Substitute.For<ICaseDirectory>();
    private readonly IAccessGuard guard = Substitute.For<IAccessGuard>();
    private readonly IPermissionAccess permissions = Substitute.For<IPermissionAccess>();
    private readonly INotificationSender notifications = Substitute.For<INotificationSender>();
    private readonly ICurrentUser caller = Substitute.For<ICurrentUser>();
    private readonly ManualTimeProvider clock = new();
    private readonly TaskManager manager;
    private readonly TaskQueryService query;
    private readonly ActivityManager activities;
    private readonly TimelineQueryService timeline;

    public TaskTests()
    {
        CallAs(Employee, TenantRole.Employee);
        data.Assignees.AddRange([(Admin, "Anna Admin"), (Employee, "Paola Neri"), (OtherEmployee, "Luca Bo")]);
        clients.FindAsync(Client, Arg.Any<CancellationToken>()).Returns(new ClientSummary(Client, "Mario Rossi", Employee));
        cases.FindAsync(ClientCase, Arg.Any<CancellationToken>()).Returns(new CaseSummary(ClientCase, "2026-00001", Client, Guid.CreateVersion7(), "730", true, true));
        guard.EnsureAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        permissions.HasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var policy = new TaskAccessPolicy(caller);
        manager = new TaskManager(ManagerHarness.Runner(), data, clients, cases, guard, policy, notifications, clock);
        query = new TaskQueryService(data, permissions, policy, SessionSettings.Tenant(), clock);
        activities = new ActivityManager(ManagerHarness.Runner(), data, clients, guard, policy, clock);
        timeline = new TimelineQueryService([new EngagementTimeline(data, permissions, policy), new FixedTimeline()], clients, permissions);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => data.DisposeAsync();

    private void CallAs(Guid userId, params TenantRole[] roles)
    {
        caller.ActorType.Returns(ActorType.User);
        caller.UserId.Returns(userId);
        caller.Roles.Returns(roles);
    }

    private static SaveTaskRequest Request(string title = "Call the client", Guid? assignee = null, DateOnly? dueOn = null, Guid? clientId = null, Guid? caseId = null) =>
        new(title, " notes ", dueOn, assignee ?? Employee, clientId, caseId);

    private TaskItem Stored(Guid id) => data.Tasks.Single(task => task.Id == id);

    [Fact]
    public async Task Create_ChecksTheReferences_AndTellsAnotherAssignee()
    {
        var invalid = await manager.CreateAsync(new SaveTaskRequest(" ", null, null, Guid.CreateVersion7(), Guid.CreateVersion7(), ClientCase), Ct);
        invalid.Error!.ValidationErrors.Keys.ShouldBe(["assigneeUserId", "clientId", "caseId"], ignoreOrder: true);
        (await manager.CreateAsync(Request(title: " "), Ct)).Error!.ValidationErrors["title"].ShouldBe(["validation.tasks.title"]);

        var mine = (await manager.CreateAsync(Request(clientId: Client, caseId: ClientCase, dueOn: Today), Ct)).Value;
        (Stored(mine).Title, Stored(mine).Notes, Stored(mine).CreatedByUserId, Stored(mine).Status).ShouldBe(("Call the client", "notes", Employee, TaskItemStatus.Open));
        await notifications.DidNotReceive().NotifyUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<NotificationMessage>(), Arg.Any<CancellationToken>());

        var given = (await manager.CreateAsync(Request(assignee: OtherEmployee, dueOn: Today), Ct)).Value;
        await notifications.Received(1).NotifyUsersAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(users => users.Single() == OtherEmployee),
            Arg.Is<NotificationMessage>(message => message.Kind == NotificationKinds.TaskAssigned && message.EntityId == given && message.Parameters["dueOn"] == "30/09/2026"),
            Arg.Any<CancellationToken>());
        NotificationKinds.Link(NotificationKinds.TaskAssigned, given).ShouldBe($"/tasks?open={given}");
    }

    [Fact]
    public async Task Employees_SeeAndChangeOnlyTheirTasks_AdministratorsEveryTask()
    {
        CallAs(Admin, TenantRole.Administrator);
        var adminOnly = (await manager.CreateAsync(Request(assignee: Admin), Ct)).Value;
        var forEmployee = (await manager.CreateAsync(Request(assignee: Employee), Ct)).Value;

        CallAs(Employee, TenantRole.Employee);
        (await query.GetAsync(adminOnly, Ct)).Error!.Code.ShouldBe(EventCodes.Requests.TaskNotFound);
        (await manager.CompleteAsync(adminOnly, Ct)).Error!.Code.ShouldBe(EventCodes.Requests.TaskNotFound);
        (await manager.CompleteAsync(forEmployee, Ct)).IsSuccess.ShouldBeTrue();
        (Stored(forEmployee).Status, Stored(forEmployee).CompletedByUserId).ShouldBe((TaskItemStatus.Done, (Guid?)Employee));
        (await manager.ReopenAsync(forEmployee, Ct)).IsSuccess.ShouldBeTrue();
        Stored(forEmployee).CompletedAt.ShouldBeNull();

        (await query.ListAsync(new TaskListQuery("all", null, null, null, null, null, 1, 25), Ct)).Value.Items.Select(task => task.Id).ShouldBe([forEmployee]);
        data.LastFilter!.VisibleTo.ShouldBe(Employee);

        CallAs(Admin, TenantRole.Administrator);
        await query.ListAsync(new TaskListQuery("all", null, null, null, null, null, 1, 25), Ct);
        (data.LastFilter!.VisibleTo, data.LastFilter.AssigneeUserId).ShouldBe(((Guid?)null, (Guid?)null));
        (await manager.DeleteAsync(forEmployee, Ct)).IsSuccess.ShouldBeTrue();
        (await query.GetAsync(forEmployee, Ct)).Error!.Code.ShouldBe(EventCodes.Requests.TaskNotFound);
    }

    [Fact]
    public async Task List_ParsesTheFilters_AndFlagsOverdueTasks()
    {
        var late = (await manager.CreateAsync(Request(dueOn: Today.AddDays(-1)), Ct)).Value;

        var list = await query.ListAsync(new TaskListQuery(null, "open", Client, ClientCase, "today", "-createdAt", 0, 500), Ct);
        (list.Value.Page, list.Value.PageSize).ShouldBe((1, TaskQueryService.MaxPageSize));
        var filter = data.LastFilter!;
        (filter.AssigneeUserId, filter.Status, filter.ClientId, filter.CaseId, filter.DueBy, filter.Sort, filter.Descending)
            .ShouldBe(((Guid?)Employee, (TaskItemStatus?)TaskItemStatus.Open, (Guid?)Client, (Guid?)ClientCase, (DateOnly?)Today, TaskSort.CreatedAt, true));
        (await query.GetAsync(late, Ct)).Value.IsOverdue.ShouldBeTrue();

        var invalid = await query.ListAsync(new TaskListQuery("everyone", "later", null, null, "tomorrow", "colour", 1, 25), Ct);
        invalid.Error!.ValidationErrors.Keys.ShouldBe(["scope", "status", "due", "sort"], ignoreOrder: true);
    }

    [Fact]
    public async Task Update_TellsTheNewAssignee()
    {
        var id = (await manager.CreateAsync(Request(), Ct)).Value;

        (await manager.UpdateAsync(id, Request(title: "Send the forms", assignee: OtherEmployee), Ct)).IsSuccess.ShouldBeTrue();
        (await manager.UpdateAsync(id, Request(title: "Send the forms", assignee: OtherEmployee), Ct)).IsSuccess.ShouldBeTrue();

        Stored(id).Title.ShouldBe("Send the forms");
        await notifications.Received(1).NotifyUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<NotificationMessage>(), Arg.Any<CancellationToken>());
        (await manager.UpdateAsync(Guid.CreateVersion7(), Request(), Ct)).Error!.Code.ShouldBe(EventCodes.Requests.TaskNotFound);
    }

    [Fact]
    public async Task Activities_AreWrittenOnClients_AndDeletedByTheirAuthorOrAnAdministrator()
    {
        (await activities.AddAsync(Guid.CreateVersion7(), new AddActivityRequest("Note", "x", null), Ct)).Error!.Code.ShouldBe(EventCodes.Directory.ClientNotFound);
        var invalid = await activities.AddAsync(Client, new AddActivityRequest("Fax", " ", clock.GetUtcNow().AddHours(2)), Ct);
        invalid.Error!.ValidationErrors.Keys.ShouldBe(["kind", "text", "occurredAt"], ignoreOrder: true);

        var id = (await activities.AddAsync(Client, new AddActivityRequest("call", " Called about the 730 ", null), Ct)).Value;
        var activity = data.Activities.Single();
        (activity.Kind, activity.Text, activity.AuthorUserId, activity.OccurredAt).ShouldBe((ActivityKind.Call, "Called about the 730", Employee, clock.GetUtcNow()));

        CallAs(OtherEmployee, TenantRole.Employee);
        (await activities.DeleteAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);
        CallAs(Admin, TenantRole.Administrator);
        (await activities.DeleteAsync(id, Ct)).IsSuccess.ShouldBeTrue();
        (await activities.DeleteAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Requests.ActivityNotFound);
    }

    [Fact]
    public async Task Timeline_JoinsTheContributors_NewestFirst()
    {
        await activities.AddAsync(Client, new AddActivityRequest("Note", "First call", clock.GetUtcNow().AddDays(-2)), Ct);
        var task = (await manager.CreateAsync(Request(clientId: Client), Ct)).Value;
        clock.Advance(TimeSpan.FromHours(1));
        await manager.CompleteAsync(task, Ct);

        var entries = (await timeline.TimelineAsync(Client, null, 10, Ct)).Value;

        entries.Select(entry => entry.Kind).ShouldBe(["task.completed", "task.created", "case.status", "activity"]);
        entries[^1].ShouldSatisfyAllConditions(
            entry => entry.TitleKey.ShouldBe("app.timeline.activity.Note"),
            entry => entry.Text.ShouldBe("First call"),
            entry => entry.CanDelete.ShouldBeTrue());
        entries[0].Link.ShouldBe($"/tasks?open={task}");
        (await timeline.TimelineAsync(Client, entries[1].At, 10, Ct)).Value.Select(entry => entry.Kind).ShouldBe(["case.status", "activity"]);
        (await timeline.TimelineAsync(Guid.CreateVersion7(), null, 10, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.ClientNotFound);

        permissions.HasAsync(EngagementPermissions.ViewActivities, Arg.Any<CancellationToken>()).Returns(false);
        (await timeline.TimelineAsync(Client, null, 10, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);
    }

    /// <summary>Another module: one entry a day ago.</summary>
    private sealed class FixedTimeline : ITimelineContributor
    {
        public Task<IReadOnlyList<TimelineEntryResponse>> EntriesAsync(Guid clientId, DateTimeOffset? before, int take, CancellationToken cancellationToken)
        {
            var at = new DateTimeOffset(2026, 9, 29, 8, 0, 0, TimeSpan.Zero);
            return Task.FromResult<IReadOnlyList<TimelineEntryResponse>>(
                before is null || at < before ? [new TimelineEntryResponse("case.status", at, "app.timeline.case.opened", new Dictionary<string, string>(), null, null, null, null, false)] : []);
        }
    }
}

/// <summary>Tasks and activities in memory (the SQL is tested on PostgreSQL).</summary>
internal sealed class InMemoryTasks : ITaskDataFactory, ITaskData
{
    public List<TaskItem> Tasks { get; } = [];

    public HashSet<Guid> Deleted { get; } = [];

    public List<ClientActivity> Activities { get; } = [];

    public List<(Guid Id, string FullName)> Assignees { get; } = [];

    public TaskFilter? LastFilter { get; private set; }

    public Task<ITaskData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<ITaskData>(this);

    /// <summary>Applies visibility and assignee only; records what was asked.</summary>
    public Task<(IReadOnlyList<TaskRow> Items, int Total)> PageAsync(TaskFilter filter, CancellationToken cancellationToken)
    {
        LastFilter = filter;
        var rows = Live()
            .Where(task => filter.VisibleTo is null || task.AssigneeUserId == filter.VisibleTo || task.CreatedByUserId == filter.VisibleTo)
            .Where(task => filter.AssigneeUserId is null || task.AssigneeUserId == filter.AssigneeUserId)
            .Select(Row)
            .ToArray();
        return Task.FromResult<(IReadOnlyList<TaskRow>, int)>((rows, rows.Length));
    }

    public Task<TaskRow?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Live().Where(task => task.Id == id).Select(Row).SingleOrDefault());

    public Task<TaskItem?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Live().SingleOrDefault(task => task.Id == id));

    public Task<(int Open, int Overdue)> CountOpenAsync(Guid userId, DateOnly today, CancellationToken cancellationToken)
    {
        var open = Live().Where(task => task.AssigneeUserId == userId && task.Status == TaskItemStatus.Open).ToArray();
        return Task.FromResult((open.Length, open.Count(task => task.IsOverdue(today))));
    }

    public Task<IReadOnlyList<ActivityRow>> ActivitiesAsync(Guid clientId, DateTimeOffset? before, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ActivityRow>>([.. Activities
            .Where(activity => activity.ClientId == clientId && (before is null || activity.OccurredAt < before))
            .OrderByDescending(activity => activity.OccurredAt)
            .Take(take)
            .Select(activity => new ActivityRow(activity.Id, activity.ClientId, activity.Kind, activity.Text, activity.OccurredAt, activity.AuthorUserId, "Author"))]);

    public Task<IReadOnlyList<TaskRow>> ClientTasksAsync(Guid clientId, Guid? visibleTo, DateTimeOffset? before, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TaskRow>>([.. Live()
            .Where(task => task.ClientId == clientId && (visibleTo is null || task.AssigneeUserId == visibleTo || task.CreatedByUserId == visibleTo))
            .Where(task => before is null || (task.CompletedAt ?? task.CreatedAt) < before)
            .Take(take)
            .Select(Row)]);

    public Task<ClientActivity?> FindActivityAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Activities.SingleOrDefault(activity => activity.Id == id));

    public Task<IReadOnlyList<(Guid Id, string FullName)>> AssigneesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<(Guid Id, string FullName)>>(Assignees);

    public void Add(TaskItem task) => Tasks.Add(task);

    public void Remove(TaskItem task) => Deleted.Add(task.Id);

    public void Add(ClientActivity activity) => Activities.Add(activity);

    public void Remove(ClientActivity activity) => Activities.Remove(activity);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private IEnumerable<TaskItem> Live() => Tasks.Where(task => !Deleted.Contains(task.Id));

    private TaskRow Row(TaskItem task) =>
        new(task.Id, task.Title, task.Notes, task.DueOn, task.Status, task.AssigneeUserId, "Assignee", task.ClientId, task.ClientId is null ? null : "Mario Rossi",
            task.CaseId, task.CaseId is null ? null : "2026-00001", task.CreatedByUserId, "Creator", task.CreatedAt, task.CompletedAt);
}
