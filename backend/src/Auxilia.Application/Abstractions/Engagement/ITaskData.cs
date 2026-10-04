using Auxilia.Domain.Engagement;

namespace Auxilia.Application.Abstractions.Engagement;

/// <summary>Sortable columns of the task list (default due date ascending, tasks without one last).</summary>
public enum TaskSort
{
    DueOn,
    CreatedAt,
    Title,
}

/// <param name="VisibleTo">Only the tasks this user is given or created; <c>null</c> for every task (Administrators).</param>
/// <param name="AssigneeUserId">Only the tasks given to this user.</param>
/// <param name="DueBy">Only open tasks due on or before this day.</param>
public sealed record TaskFilter(
    Guid? VisibleTo, Guid? AssigneeUserId, TaskItemStatus? Status, Guid? ClientId, Guid? CaseId, DateOnly? DueBy, TaskSort Sort, bool Descending, int Skip, int Take);

public sealed record TaskRow(
    Guid Id,
    string Title,
    string? Notes,
    DateOnly? DueOn,
    TaskItemStatus Status,
    Guid AssigneeUserId,
    string AssigneeName,
    Guid? ClientId,
    string? ClientName,
    Guid? CaseId,
    string? CaseNumber,
    Guid CreatedByUserId,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public sealed record ActivityRow(Guid Id, Guid ClientId, ActivityKind Kind, string Text, DateTimeOffset OccurredAt, Guid AuthorUserId, string AuthorName);

/// <summary>Tasks and client activities of the current tenant (B-26), one unit of work; deleted tasks are never returned.</summary>
public interface ITaskData : IAsyncDisposable
{
    Task<(IReadOnlyList<TaskRow> Items, int Total)> PageAsync(TaskFilter filter, CancellationToken cancellationToken);

    Task<TaskRow?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Tracked.</summary>
    Task<TaskItem?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Open tasks of the user (given to them), and how many are overdue on <paramref name="today"/>.</summary>
    Task<(int Open, int Overdue)> CountOpenAsync(Guid userId, DateOnly today, CancellationToken cancellationToken);

    /// <summary>The activities of a client, newest first, before an instant.</summary>
    Task<IReadOnlyList<ActivityRow>> ActivitiesAsync(Guid clientId, DateTimeOffset? before, int take, CancellationToken cancellationToken);

    /// <summary>The tasks about a client (any status), newest change first, before an instant (created or completed).</summary>
    Task<IReadOnlyList<TaskRow>> ClientTasksAsync(Guid clientId, Guid? visibleTo, DateTimeOffset? before, int take, CancellationToken cancellationToken);

    /// <summary>Tracked.</summary>
    Task<ClientActivity?> FindActivityAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Active users with the Administrator or Employee role, by name: who a task can be given to.</summary>
    Task<IReadOnlyList<(Guid Id, string FullName)>> AssigneesAsync(CancellationToken cancellationToken);

    void Add(TaskItem task);

    void Remove(TaskItem task);

    void Add(ClientActivity activity);

    void Remove(ClientActivity activity);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ITaskDataFactory
{
    Task<ITaskData> OpenAsync(CancellationToken cancellationToken);
}
