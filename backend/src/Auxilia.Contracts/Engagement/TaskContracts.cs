namespace Auxilia.Contracts.Engagement;

/// <summary>A task (B-26): <c>assigneeUserId</c> an active Administrator or Employee; <c>caseId</c> a case of <c>clientId</c>.</summary>
public sealed record SaveTaskRequest(string Title, string? Notes, DateOnly? DueOn, Guid AssigneeUserId, Guid? ClientId, Guid? CaseId);

/// <param name="Status"><c>Open</c> or <c>Done</c>.</param>
/// <param name="IsOverdue">Open and due before today (tenant time zone).</param>
public sealed record TaskResponse(
    Guid Id,
    string Title,
    string? Notes,
    DateOnly? DueOn,
    string Status,
    bool IsOverdue,
    TaskUserResponse Assignee,
    TaskClientResponse? Client,
    TaskCaseResponse? Case,
    TaskUserResponse CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public sealed record TaskUserResponse(Guid Id, string FullName);

public sealed record TaskClientResponse(Guid Id, string FullName);

public sealed record TaskCaseResponse(Guid Id, string Number);

/// <summary>
/// The task list: <c>scope</c> <c>mine</c> (given to me, default) or <c>all</c> (what I may see: Administrators every
/// task, employees the ones given to them or created by them); <c>status</c> <c>Open</c>/<c>Done</c>; <c>clientId</c>,
/// <c>caseId</c>; <c>due</c> <c>today</c> (open, due today or before); <c>sort</c> <c>dueOn</c> (default),
/// <c>createdAt</c>, <c>title</c>, <c>-</c> for descending.
/// </summary>
public sealed record TaskListQuery(string? Scope, string? Status, Guid? ClientId, Guid? CaseId, string? Due, string? Sort, int Page, int PageSize);

/// <summary>Staff a task can be given to (active Administrators and Employees), by name.</summary>
public sealed record TaskAssigneeResponse(Guid Id, string FullName);

/// <param name="Kind"><c>Note</c>, <c>Call</c>, <c>Meeting</c> or <c>Email</c>.</param>
/// <param name="OccurredAt">When it happened; now when not given.</param>
public sealed record AddActivityRequest(string Kind, string Text, DateTimeOffset? OccurredAt);

/// <summary>
/// An entry of the client timeline (B-26), newest first: <c>kind</c> <c>activity</c>, <c>task.created</c>,
/// <c>task.completed</c>, <c>case.status</c>, <c>case.payment</c>; <c>titleKey</c> a translation key with
/// <c>parameters</c>; <c>text</c> the note of an activity; <c>activityId</c> and <c>canDelete</c> for activities.
/// </summary>
public sealed record TimelineEntryResponse(
    string Kind,
    DateTimeOffset At,
    string TitleKey,
    IReadOnlyDictionary<string, string> Parameters,
    string? Text,
    string? ActorName,
    string? Link,
    Guid? ActivityId,
    bool CanDelete);

public sealed record AddActivityResponse(Guid Id);
