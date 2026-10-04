using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Engagement;

public enum TaskItemStatus
{
    Open,
    Done,
}

/// <summary>Title, notes, due date and links of a task (validated together).</summary>
public sealed record TaskDetails(string? Title, string? Notes, DateOnly? DueOn, Guid AssigneeUserId, Guid? ClientId, Guid? CaseId);

/// <summary>
/// A task of the staff (<c>engagement.tasks</c>, B-26, F27): something to do by a date for a user, optionally about a
/// client and one of its cases. No reminders (D-15): the dashboards and the task list show what is due. Deletes are soft.
/// </summary>
public sealed class TaskItem : AggregateRoot<Guid>, IAuditable, ISoftDeletable
{
    public const int TitleMaxLength = 200;
    public const int NotesMaxLength = 2000;

    private TaskItem(Guid id, Guid createdByUserId, DateTimeOffset now)
        : base(id)
    {
        CreatedByUserId = createdByUserId;
        CreatedAt = now;
        Status = TaskItemStatus.Open;
        Title = string.Empty;
    }

    private TaskItem()
    {
        Title = string.Empty;
    }

    public string Title { get; private set; }

    public string? Notes { get; private set; }

    public DateOnly? DueOn { get; private set; }

    public Guid AssigneeUserId { get; private set; }

    public Guid? ClientId { get; private set; }

    public Guid? CaseId { get; private set; }

    public TaskItemStatus Status { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public Guid? CompletedByUserId { get; private set; }

    public static Result<TaskItem> Create(Guid id, TaskDetails details, Guid createdByUserId, DateTimeOffset now)
    {
        var task = new TaskItem(id, createdByUserId, now);
        var applied = task.Update(details);
        return applied.IsFailure ? Result.Failure<TaskItem>(applied.Error!) : task;
    }

    /// <summary>Checks every value (one error per field) and replaces them.</summary>
    public Result Update(TaskDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var title = details.Title?.Trim() ?? string.Empty;
        if (title.Length is 0 or > TitleMaxLength)
        {
            errors["title"] = ["validation.tasks.title"];
        }

        var notes = string.IsNullOrWhiteSpace(details.Notes) ? null : details.Notes.Trim();
        if (notes is { Length: > NotesMaxLength })
        {
            errors["notes"] = ["validation.tasks.notes"];
        }

        if (details.CaseId is not null && details.ClientId is null)
        {
            errors["caseId"] = ["validation.tasks.case"];
        }

        if (errors.Count > 0)
        {
            return Errors.Requests.TaskInvalid(errors);
        }

        Title = title;
        Notes = notes;
        DueOn = details.DueOn;
        AssigneeUserId = details.AssigneeUserId;
        ClientId = details.ClientId;
        CaseId = details.CaseId;
        return Result.Success();
    }

    /// <returns>False when it is already done.</returns>
    public bool Complete(Guid? userId, DateTimeOffset now)
    {
        if (Status == TaskItemStatus.Done)
        {
            return false;
        }

        Status = TaskItemStatus.Done;
        CompletedAt = now;
        CompletedByUserId = userId;
        return true;
    }

    /// <returns>False when it is still open.</returns>
    public bool Reopen()
    {
        if (Status == TaskItemStatus.Open)
        {
            return false;
        }

        Status = TaskItemStatus.Open;
        CompletedAt = null;
        CompletedByUserId = null;
        return true;
    }

    /// <summary>Open and due before <paramref name="today"/>.</summary>
    public bool IsOverdue(DateOnly today) => Status == TaskItemStatus.Open && DueOn < today;
}

public enum ActivityKind
{
    Note,
    Call,
    Meeting,
    Email,
}

/// <summary>
/// Something done with a client that staff write down (<c>engagement.activities</c>, B-26): a note, a call, a meeting
/// or an e-mail, with when it happened. Shown in the client timeline with the events of the other modules.
/// </summary>
public sealed class ClientActivity : AggregateRoot<Guid>, IAuditable
{
    public const int TextMaxLength = 2000;

    private ClientActivity(Guid id, Guid clientId, ActivityKind kind, string text, DateTimeOffset occurredAt, Guid authorUserId)
        : base(id)
    {
        ClientId = clientId;
        Kind = kind;
        Text = text;
        OccurredAt = occurredAt;
        AuthorUserId = authorUserId;
    }

    private ClientActivity()
    {
        Text = string.Empty;
    }

    public Guid ClientId { get; private set; }

    public ActivityKind Kind { get; private set; }

    public string Text { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public Guid AuthorUserId { get; private set; }

    /// <param name="occurredAt">When it happened; not in the future (a minute of clock skew allowed).</param>
    public static Result<ClientActivity> Create(Guid id, Guid clientId, ActivityKind kind, string? text, DateTimeOffset occurredAt, Guid authorUserId, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > TextMaxLength)
        {
            errors["text"] = ["validation.activities.text"];
        }

        if (!Enum.IsDefined(kind))
        {
            errors["kind"] = ["validation.activities.kind"];
        }

        if (occurredAt > now.AddMinutes(1))
        {
            errors["occurredAt"] = ["validation.activities.occurredAt"];
        }

        return errors.Count > 0
            ? Errors.Requests.ActivityInvalid(errors)
            : new ClientActivity(id, clientId, kind, trimmed, occurredAt, authorUserId);
    }
}
