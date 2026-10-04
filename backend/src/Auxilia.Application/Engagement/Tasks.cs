using System.Globalization;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Engagement;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Abstractions.Timeline;
using Auxilia.Application.Cases.Public;
using Auxilia.Application.Directory.Public;
using Auxilia.Application.Engagement.Public;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Engagement;
using Auxilia.Diagnostics;
using Auxilia.Domain.Engagement;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Engagement;

/// <summary>
/// Tasks of the staff (B-26, F27): something to do by a date, given to an Administrator or an Employee, optionally
/// about a client and one of its cases. No reminders (D-15): due tasks show on the dashboard and in the task list.
/// </summary>
public interface ITaskManager
{
    /// <summary>The assignee is told by a notification unless it is the caller.</summary>
    Task<Result<Guid>> CreateAsync(SaveTaskRequest request, CancellationToken cancellationToken);

    Task<Result> UpdateAsync(Guid id, SaveTaskRequest request, CancellationToken cancellationToken);

    Task<Result> CompleteAsync(Guid id, CancellationToken cancellationToken);

    Task<Result> ReopenAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Soft delete.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface ITaskQueryService
{
    Task<Result<PagedResponse<TaskResponse>>> ListAsync(TaskListQuery query, CancellationToken cancellationToken);

    /// <summary><c>AUX-17014</c> when the caller cannot see it.</summary>
    Task<Result<TaskResponse>> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskAssigneeResponse>> AssigneesAsync(CancellationToken cancellationToken);
}

/// <summary>Notes, calls, meetings and e-mails written on a client (B-26).</summary>
public interface IActivityManager
{
    Task<Result<Guid>> AddAsync(Guid clientId, AddActivityRequest request, CancellationToken cancellationToken);

    /// <summary>Its author or an Administrator.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>The timeline of a client (B-26): the activities and the events of the modules, newest first.</summary>
public interface ITimelineQueryService
{
    /// <param name="before">Entries before this instant (the <c>at</c> of the last entry of the previous page).</param>
    Task<Result<IReadOnlyList<TimelineEntryResponse>>> TimelineAsync(Guid clientId, DateTimeOffset? before, int take, CancellationToken cancellationToken);
}

/// <summary>Who sees and changes a task: Administrators every task, employees the ones given to them or created by them.</summary>
internal sealed class TaskAccessPolicy(ICurrentUser currentUser)
{
    public bool IsAdministrator => Has(TenantRole.Administrator);

    public Guid? Me => currentUser.ActorType == ActorType.User ? currentUser.UserId : null;

    /// <summary>The filter of the lists: <c>null</c> = everything.</summary>
    public Guid? VisibleTo => IsAdministrator ? null : Me ?? Guid.Empty;

    public bool CanSee(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return IsAdministrator || (Me is { } me && (task.AssigneeUserId == me || task.CreatedByUserId == me));
    }

    private bool Has(TenantRole role) => currentUser.ActorType == ActorType.User && currentUser.Roles.Contains(role);
}

/// <summary>Today in the tenant time zone (due dates are local days).</summary>
internal static class TenantToday
{
    public static DateOnly Of(ITenantContext tenant, TimeProvider clock)
    {
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(tenant.Tenant.TimeZone, out var found) ? found : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
    }
}

internal sealed class TaskManager(
    IOperationRunner operations,
    ITaskDataFactory data,
    IClientDirectory clients,
    ICaseDirectory cases,
    IAccessGuard guard,
    TaskAccessPolicy policy,
    INotificationSender notifications,
    TimeProvider clock) : ITaskManager
{
    public Task<Result<Guid>> CreateAsync(SaveTaskRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<Guid>(Operations.Requests.CreateTask, null, async scope =>
        {
            var allowed = await guard.EnsureAsync(EngagementPermissions.ManageTasks, cancellationToken);
            if (allowed.IsFailure || policy.Me is not { } me)
            {
                return Result.Failure<Guid>(allowed.Error ?? Errors.Identity.PermissionDenied());
            }

            await using var store = await data.OpenAsync(cancellationToken);
            var references = await CheckReferencesAsync(store, request, cancellationToken);
            if (references.IsFailure)
            {
                return Result.Failure<Guid>(references.Error!);
            }

            var created = TaskItem.Create(Guid.CreateVersion7(), Details(request), me, clock.GetUtcNow());
            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error!);
            }

            store.Add(created.Value);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity("Task", created.Value.Id);
            await TellAssigneeAsync(created.Value, me, cancellationToken);
            return created.Value.Id;
        }, cancellationToken);
    }

    public Task<Result> UpdateAsync(Guid id, SaveTaskRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ChangeAsync(Operations.Requests.UpdateTask, id, async (store, task) =>
        {
            var references = await CheckReferencesAsync(store, request, cancellationToken);
            if (references.IsFailure)
            {
                return references;
            }

            var previousAssignee = task.AssigneeUserId;
            var updated = task.Update(Details(request));
            if (updated.IsSuccess && task.AssigneeUserId != previousAssignee)
            {
                await TellAssigneeAsync(task, policy.Me, cancellationToken);
            }

            return updated;
        }, cancellationToken);
    }

    public Task<Result> CompleteAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(Operations.Requests.CompleteTask, id, (_, task) =>
        {
            task.Complete(policy.Me, clock.GetUtcNow());
            return Task.FromResult(Result.Success());
        }, cancellationToken);

    public Task<Result> ReopenAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(Operations.Requests.ReopenTask, id, (_, task) =>
        {
            task.Reopen();
            return Task.FromResult(Result.Success());
        }, cancellationToken);

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(Operations.Requests.DeleteTask, id, (store, task) =>
        {
            store.Remove(task);
            return Task.FromResult(Result.Success());
        }, cancellationToken);

    private static TaskDetails Details(SaveTaskRequest request) =>
        new(request.Title, request.Notes, request.DueOn, request.AssigneeUserId, request.ClientId, request.CaseId);

    private Task<Result> ChangeAsync(OperationDescriptor operation, Guid id, Func<ITaskData, TaskItem, Task<Result>> change, CancellationToken cancellationToken) =>
        operations.RunAsync(operation, new { TaskId = id }, async _ =>
        {
            var allowed = await guard.EnsureAsync(EngagementPermissions.ManageTasks, cancellationToken);
            if (allowed.IsFailure)
            {
                return allowed;
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindAsync(id, cancellationToken) is not { } task || !policy.CanSee(task))
            {
                return Errors.Requests.TaskNotFound();
            }

            var changed = await change(store, task);
            if (changed.IsFailure)
            {
                return changed;
            }

            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    /// <summary>The assignee is active staff, the client exists, the case is one of the client's that the caller sees.</summary>
    private async Task<Result> CheckReferencesAsync(ITaskData store, SaveTaskRequest request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!(await store.AssigneesAsync(cancellationToken)).Any(item => item.Id == request.AssigneeUserId))
        {
            errors["assigneeUserId"] = ["validation.tasks.assignee"];
        }

        if (request.ClientId is { } clientId && await clients.FindAsync(clientId, cancellationToken) is null)
        {
            errors["clientId"] = ["validation.tasks.client"];
        }

        if (request.CaseId is { } caseId
            && (await cases.FindAsync(caseId, cancellationToken) is not { CanSee: true } found || found.ClientId != request.ClientId))
        {
            errors["caseId"] = ["validation.tasks.case"];
        }

        return errors.Count > 0 ? Errors.Requests.TaskInvalid(errors) : Result.Success();
    }

    private async Task TellAssigneeAsync(TaskItem task, Guid? actor, CancellationToken cancellationToken)
    {
        if (task.AssigneeUserId == actor)
        {
            return;
        }

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["title"] = task.Title,
            ["dueOn"] = task.DueOn?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "—",
        };
        await notifications.NotifyUsersAsync([task.AssigneeUserId], new NotificationMessage(NotificationKinds.TaskAssigned, task.Id, parameters), cancellationToken);
    }
}

internal sealed class TaskQueryService(ITaskDataFactory data, IPermissionAccess permissions, TaskAccessPolicy policy, ITenantContext tenant, TimeProvider clock)
    : ITaskQueryService
{
    public const int MaxPageSize = 100;

    public async Task<Result<PagedResponse<TaskResponse>>> ListAsync(TaskListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await permissions.HasAsync(EngagementPermissions.ViewTasks, cancellationToken) || policy.Me is not { } me)
        {
            return Errors.Identity.PermissionDenied();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var mine = query.Scope?.ToUpperInvariant() switch
        {
            null or "MINE" => true,
            "ALL" => false,
            _ => Invalid("scope", true),
        };
        TaskItemStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            status = Enum.TryParse<TaskItemStatus>(query.Status, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : Invalid<TaskItemStatus?>("status", null);
        }

        var today = TenantToday.Of(tenant, clock);
        DateOnly? dueBy = query.Due?.ToUpperInvariant() switch
        {
            null => null,
            "TODAY" => today,
            _ => Invalid<DateOnly?>("due", null),
        };
        var (sort, descending) = query.Sort?.TrimStart('-').ToUpperInvariant() switch
        {
            null or "DUEON" => (TaskSort.DueOn, query.Sort?.StartsWith('-') == true),
            "CREATEDAT" => (TaskSort.CreatedAt, query.Sort.StartsWith('-')),
            "TITLE" => (TaskSort.Title, query.Sort.StartsWith('-')),
            _ => (Invalid<TaskSort>("sort", TaskSort.DueOn), false),
        };
        if (errors.Count > 0)
        {
            return Errors.Requests.TaskInvalid(errors);
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        await using var store = await data.OpenAsync(cancellationToken);
        var (items, total) = await store.PageAsync(
            new TaskFilter(policy.VisibleTo, mine ? me : null, status, query.ClientId, query.CaseId, dueBy, sort, descending, (page - 1) * pageSize, pageSize),
            cancellationToken);
        return new PagedResponse<TaskResponse>([.. items.Select(row => ToResponse(row, today))], page, pageSize, total);

        T Invalid<T>(string field, T fallback)
        {
            errors[field] = [$"validation.tasks.{field}"];
            return fallback;
        }
    }

    public async Task<Result<TaskResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await permissions.HasAsync(EngagementPermissions.ViewTasks, cancellationToken))
        {
            return Errors.Identity.PermissionDenied();
        }

        await using var store = await data.OpenAsync(cancellationToken);
        var row = await store.GetAsync(id, cancellationToken);
        return row is not null && (policy.VisibleTo is null || row.AssigneeUserId == policy.VisibleTo || row.CreatedByUserId == policy.VisibleTo)
            ? ToResponse(row, TenantToday.Of(tenant, clock))
            : Errors.Requests.TaskNotFound();
    }

    public async Task<IReadOnlyList<TaskAssigneeResponse>> AssigneesAsync(CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return [.. (await store.AssigneesAsync(cancellationToken)).Select(item => new TaskAssigneeResponse(item.Id, item.FullName))];
    }

    internal static TaskResponse ToResponse(TaskRow row, DateOnly today) =>
        new(
            row.Id,
            row.Title,
            row.Notes,
            row.DueOn,
            row.Status.ToString(),
            row.Status == TaskItemStatus.Open && row.DueOn < today,
            new TaskUserResponse(row.AssigneeUserId, row.AssigneeName),
            row.ClientId is { } clientId ? new TaskClientResponse(clientId, row.ClientName ?? string.Empty) : null,
            row.CaseId is { } caseId ? new TaskCaseResponse(caseId, row.CaseNumber ?? string.Empty) : null,
            new TaskUserResponse(row.CreatedByUserId, row.CreatedByName),
            row.CreatedAt,
            row.CompletedAt);
}

internal sealed class ActivityManager(
    IOperationRunner operations,
    ITaskDataFactory data,
    IClientDirectory clients,
    IAccessGuard guard,
    TaskAccessPolicy policy,
    TimeProvider clock) : IActivityManager
{
    public Task<Result<Guid>> AddAsync(Guid clientId, AddActivityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<Guid>(Operations.Requests.AddActivity, new { ClientId = clientId }, async scope =>
        {
            var allowed = await guard.EnsureAsync(EngagementPermissions.ManageActivities, cancellationToken);
            if (allowed.IsFailure || policy.Me is not { } me)
            {
                return Result.Failure<Guid>(allowed.Error ?? Errors.Identity.PermissionDenied());
            }

            if (await clients.FindAsync(clientId, cancellationToken) is null)
            {
                return Errors.Directory.ClientNotFound();
            }

            var now = clock.GetUtcNow();
            var kind = Enum.TryParse<ActivityKind>(request.Kind, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : (ActivityKind)(-1);
            var created = ClientActivity.Create(Guid.CreateVersion7(), clientId, kind, request.Text, request.OccurredAt ?? now, me, now);
            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error!);
            }

            await using var store = await data.OpenAsync(cancellationToken);
            store.Add(created.Value);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity("Activity", created.Value.Id);
            return created.Value.Id;
        }, cancellationToken);
    }

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Requests.DeleteActivity, new { ActivityId = id }, async _ =>
        {
            var allowed = await guard.EnsureAsync(EngagementPermissions.ManageActivities, cancellationToken);
            if (allowed.IsFailure)
            {
                return allowed;
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindActivityAsync(id, cancellationToken) is not { } activity)
            {
                return Errors.Requests.ActivityNotFound();
            }

            if (!policy.IsAdministrator && activity.AuthorUserId != policy.Me)
            {
                return Errors.Identity.PermissionDenied();
            }

            store.Remove(activity);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
}

/// <summary>The activities and the tasks of a client in its timeline.</summary>
internal sealed class EngagementTimeline(ITaskDataFactory data, IPermissionAccess permissions, TaskAccessPolicy policy) : ITimelineContributor
{
    public async Task<IReadOnlyList<TimelineEntryResponse>> EntriesAsync(Guid clientId, DateTimeOffset? before, int take, CancellationToken cancellationToken)
    {
        var entries = new List<TimelineEntryResponse>();
        await using var store = await data.OpenAsync(cancellationToken);
        foreach (var activity in await store.ActivitiesAsync(clientId, before, take, cancellationToken))
        {
            entries.Add(new TimelineEntryResponse(
                "activity",
                activity.OccurredAt,
                $"app.timeline.activity.{activity.Kind}",
                new Dictionary<string, string>(StringComparer.Ordinal),
                activity.Text,
                activity.AuthorName,
                null,
                activity.Id,
                policy.IsAdministrator || activity.AuthorUserId == policy.Me));
        }

        if (await permissions.HasAsync(EngagementPermissions.ViewTasks, cancellationToken))
        {
            foreach (var task in await store.ClientTasksAsync(clientId, policy.VisibleTo, before, take, cancellationToken))
            {
                var parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["title"] = task.Title };
                var link = $"/tasks?open={task.Id}";
                if (task.CompletedAt is { } completedAt && (before is null || completedAt < before))
                {
                    entries.Add(new TimelineEntryResponse("task.completed", completedAt, "app.timeline.task.completed", parameters, null, null, link, null, false));
                }

                if (before is null || task.CreatedAt < before)
                {
                    entries.Add(new TimelineEntryResponse("task.created", task.CreatedAt, "app.timeline.task.created", parameters, task.Notes, task.CreatedByName, link, null, false));
                }
            }
        }

        return entries;
    }
}

internal sealed class TimelineQueryService(
    IEnumerable<ITimelineContributor> contributors, IClientDirectory clients, IPermissionAccess permissions) : ITimelineQueryService
{
    public const int MaxTake = 100;

    public async Task<Result<IReadOnlyList<TimelineEntryResponse>>> TimelineAsync(Guid clientId, DateTimeOffset? before, int take, CancellationToken cancellationToken)
    {
        if (!await permissions.HasAsync(EngagementPermissions.ViewActivities, cancellationToken))
        {
            return Errors.Identity.PermissionDenied();
        }

        if (await clients.FindAsync(clientId, cancellationToken) is null)
        {
            return Errors.Directory.ClientNotFound();
        }

        take = Math.Clamp(take, 1, MaxTake);
        var entries = new List<TimelineEntryResponse>();
        foreach (var contributor in contributors)
        {
            entries.AddRange(await contributor.EntriesAsync(clientId, before, take, cancellationToken));
        }

        return entries.OrderByDescending(entry => entry.At).Take(take).ToArray();
    }
}
