using Auxilia.SharedKernel.Domain;

namespace Auxilia.Domain.Mailbox;

/// <summary>What a synchronisation copies from an account (N05, #106): message headers or calendar events.</summary>
public enum SyncScope
{
    Mail,
    Calendar,
}

/// <summary>
/// Why a synchronisation runs: the Mail or Calendar page was opened, "Sync now", or a future scheduled batch
/// (<c>Scheduled</c> exists for it and is not used: no scheduled job, D-15).
/// </summary>
public enum SyncTrigger
{
    PageOpen,
    Manual,
    Scheduled,
}

public enum SyncRunStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
}

/// <summary>
/// One synchronisation of an account and scope (<c>mailbox.sync_runs</c>, ADR 0021): queued by the requester, run by
/// the Worker in chunks (each chunk saved with the cursor), continued in further messages when it takes long. At most
/// one queued or running per account and scope (partial unique index); a run without progress for
/// <see cref="StaleAfter"/> is abandoned (its message was dead-lettered or the Worker stopped).
/// </summary>
public sealed class SyncRun : AggregateRoot<Guid>
{
    public const int ErrorCodeMaxLength = 20;
    public const int TraceIdMaxLength = 64;

    /// <summary>No progress for this long: the run is considered abandoned and a new one may start.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

    private SyncRun(Guid id, Guid accountId, Guid userId, SyncScope scope, SyncTrigger trigger, DateTimeOffset now)
        : base(id)
    {
        AccountId = accountId;
        UserId = userId;
        Scope = scope;
        Trigger = trigger;
        Status = SyncRunStatus.Queued;
        QueuedAt = now;
        UpdatedAt = now;
        Parts = 1;
    }

    private SyncRun()
    {
    }

    public Guid AccountId { get; private set; }

    /// <summary>The owner of the account, who asked for the run (or on whose behalf a batch did).</summary>
    public Guid UserId { get; private set; }

    public SyncScope Scope { get; private set; }

    public SyncTrigger Trigger { get; private set; }

    public SyncRunStatus Status { get; private set; }

    public DateTimeOffset QueuedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    /// <summary>Last progress (start, chunk, continuation, end): a run without progress for <see cref="StaleAfter"/> is abandoned.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary><c>AUX-…</c> code of the failure, or of the last transient error while it is retried.</summary>
    public string? ErrorCode { get; private set; }

    public string? TraceId { get; private set; }

    public int NewCount { get; private set; }

    public int UpdatedCount { get; private set; }

    /// <summary>Malformed items skipped (counted, never fatal).</summary>
    public int SkippedCount { get; private set; }

    /// <summary>Messages the run took: 1, plus one per continuation queued when the time budget ran out.</summary>
    public int Parts { get; private set; }

    /// <summary>
    /// Mail that arrived since the previous sync (new messages of incremental syncs, not those of a first or full sync):
    /// the owner is notified only when it is not zero (N05 #106).
    /// </summary>
    public int ArrivedCount { get; private set; }

    public bool IsActive => Status is SyncRunStatus.Queued or SyncRunStatus.Running;

    public static SyncRun Queue(Guid id, Guid accountId, Guid userId, SyncScope scope, SyncTrigger trigger, DateTimeOffset now) =>
        new(id, accountId, userId, scope, trigger, now);

    public bool IsStale(DateTimeOffset now) => IsActive && now - UpdatedAt >= StaleAfter;

    /// <summary>Running, or ended less than <paramref name="minInterval"/> ago: a new request queues nothing.</summary>
    public bool IsRecent(DateTimeOffset now, TimeSpan minInterval) =>
        (IsActive && !IsStale(now)) || (EndedAt is { } ended && now - ended < minInterval);

    public void Start(DateTimeOffset now)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException($"A {Status} sync run cannot start.");
        }

        Status = SyncRunStatus.Running;
        StartedAt ??= now;
        UpdatedAt = now;
    }

    public void Record(int added, int updated, int skipped, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(added);
        ArgumentOutOfRangeException.ThrowIfNegative(updated);
        ArgumentOutOfRangeException.ThrowIfNegative(skipped);
        NewCount += added;
        UpdatedCount += updated;
        SkippedCount += skipped;
        UpdatedAt = now;
    }

    /// <summary>New messages of an incremental sync (part of <see cref="NewCount"/>, recorded with them).</summary>
    public void RecordArrived(int arrived)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(arrived);
        ArrivedCount += arrived;
    }

    /// <summary>The time budget of this message ran out: the rest goes on in a new message.</summary>
    public void Continue(DateTimeOffset now)
    {
        EnsureRunning();
        Parts++;
        UpdatedAt = now;
    }

    /// <summary>A transient failure the bus will retry: kept on the run so the owner sees why it is late.</summary>
    public void NoteTransientFailure(string errorCode, string? traceId, DateTimeOffset now)
    {
        EnsureRunning();
        ErrorCode = Bounded(errorCode, ErrorCodeMaxLength);
        TraceId = Bounded(traceId, TraceIdMaxLength);
        UpdatedAt = now;
    }

    public void Succeed(DateTimeOffset now)
    {
        EnsureRunning();
        Status = SyncRunStatus.Succeeded;
        ErrorCode = null;
        TraceId = null;
        EndedAt = now;
        UpdatedAt = now;
    }

    public void Fail(string errorCode, string? traceId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(errorCode);
        if (!IsActive)
        {
            throw new InvalidOperationException($"A {Status} sync run cannot fail.");
        }

        Status = SyncRunStatus.Failed;
        ErrorCode = Bounded(errorCode, ErrorCodeMaxLength);
        TraceId = Bounded(traceId, TraceIdMaxLength);
        EndedAt = now;
        UpdatedAt = now;
    }

    private void EnsureRunning()
    {
        if (Status != SyncRunStatus.Running)
        {
            throw new InvalidOperationException($"The sync run is {Status}, not running.");
        }
    }

    private static string? Bounded(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? null : value.Length > maxLength ? value[..maxLength] : value;
}

/// <summary>
/// Where the synchronisation of an account, scope and folder stands (<c>mailbox.sync_cursors</c>): the page token of a
/// sync in progress and the provider's token for the next incremental sync (Google <c>syncToken</c>, Graph
/// <c>deltaLink</c>, IMAP UID + UIDVALIDITY, Gmail <c>historyId</c>). Saved in the transaction of each chunk, so an
/// interrupted sync resumes from the last saved chunk.
/// </summary>
public sealed class SyncCursor
{
    public const int FolderKeyMaxLength = 200;
    public const int TokenMaxLength = 8000;

    /// <summary>The folder key of a scope without folders (the primary calendar).</summary>
    public const string Primary = "primary";

    private SyncCursor(Guid accountId, SyncScope scope, string folderKey, DateTimeOffset now)
    {
        AccountId = accountId;
        Scope = scope;
        FolderKey = folderKey;
        FullSyncStartedAt = now;
        UpdatedAt = now;
    }

    private SyncCursor()
    {
        FolderKey = Primary;
    }

    public Guid AccountId { get; private set; }

    public SyncScope Scope { get; private set; }

    public string FolderKey { get; private set; }

    /// <summary>The next page of the sync in progress; null between syncs.</summary>
    public string? PageToken { get; private set; }

    /// <summary>The provider's token for the next incremental sync; null until a full sync completed.</summary>
    public string? SyncToken { get; private set; }

    /// <summary>When the current full sync started: items not seen since then are gone once it completes.</summary>
    public DateTimeOffset FullSyncStartedAt { get; private set; }

    /// <summary>A full sync is in progress (first sync, expired token, window moved).</summary>
    public bool IsFullSync => SyncToken is null;

    public DateTimeOffset UpdatedAt { get; private set; }

    public static SyncCursor Begin(Guid accountId, SyncScope scope, string folderKey, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(folderKey);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(folderKey.Length, FolderKeyMaxLength);
        return new SyncCursor(accountId, scope, folderKey, now);
    }

    /// <summary>A page was saved; more pages follow.</summary>
    public void Advance(string nextPageToken, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(nextPageToken);
        PageToken = Checked(nextPageToken);
        UpdatedAt = now;
    }

    /// <summary>The last page was saved: the next sync starts from <paramref name="syncToken"/>.</summary>
    public void Complete(string syncToken, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(syncToken);
        PageToken = null;
        SyncToken = Checked(syncToken);
        UpdatedAt = now;
    }

    /// <summary>The provider no longer accepts the token (or the window moved): a bounded full sync starts again.</summary>
    public void Reset(DateTimeOffset now)
    {
        PageToken = null;
        SyncToken = null;
        FullSyncStartedAt = now;
        UpdatedAt = now;
    }

    private static string Checked(string token) =>
        token.Length <= TokenMaxLength ? token : throw new ArgumentOutOfRangeException(nameof(token), "The provider's token is too long.");
}

/// <summary>An event read from a provider, before it is stored (adapters fill it, the domain bounds it).</summary>
/// <param name="StartsAt">For all-day events, the first day at 00:00 UTC.</param>
/// <param name="EndsAt">Exclusive; for all-day events, the day after the last one at 00:00 UTC.</param>
public sealed record CalendarEventData(
    string ProviderEventId, string? Title, string? Location, DateTimeOffset StartsAt, DateTimeOffset EndsAt, bool AllDay, bool Tentative, string? Link);

/// <summary>
/// An event of an external calendar (<c>mailbox.calendar_events</c>, N05 #101): read-only, visible only to the owner of
/// the account, kept for the sync window. Unique per account and provider event id, so a chunk saved twice changes
/// nothing.
/// </summary>
public sealed class CalendarEvent : Entity<Guid>
{
    public const int ProviderEventIdMaxLength = 1024;
    public const int TitleMaxLength = 500;
    public const int LocationMaxLength = 500;
    public const int LinkMaxLength = 2048;

    private CalendarEvent(Guid id, Guid accountId, string providerEventId)
        : base(id)
    {
        AccountId = accountId;
        ProviderEventId = providerEventId;
        Title = string.Empty;
    }

    private CalendarEvent()
    {
        ProviderEventId = string.Empty;
        Title = string.Empty;
    }

    public Guid AccountId { get; private set; }

    public string ProviderEventId { get; private set; }

    public string Title { get; private set; }

    public string? Location { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public bool AllDay { get; private set; }

    public bool Tentative { get; private set; }

    /// <summary>The event at the provider (opened in a new tab); only <c>https</c> links are kept.</summary>
    public string? Link { get; private set; }

    /// <summary>Last chunk that carried the event: a full sync removes the events it did not see.</summary>
    public DateTimeOffset SyncedAt { get; private set; }

    /// <summary>Whether the data can be stored: an id and an end not before the start (otherwise the item is skipped and counted).</summary>
    public static bool IsUsable(CalendarEventData data) =>
        data is not null
        && !string.IsNullOrWhiteSpace(data.ProviderEventId)
        && data.ProviderEventId.Length <= ProviderEventIdMaxLength
        && data.EndsAt >= data.StartsAt;

    public static CalendarEvent Create(Guid id, Guid accountId, CalendarEventData data, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!IsUsable(data))
        {
            throw new ArgumentException("The event is not usable.", nameof(data));
        }

        var created = new CalendarEvent(id, accountId, data.ProviderEventId);
        created.Apply(data, now);
        return created;
    }

    /// <returns>Whether anything shown changed.</returns>
    public bool Apply(CalendarEventData data, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        var title = Bounded(data.Title, TitleMaxLength) ?? string.Empty;
        var location = Bounded(data.Location, LocationMaxLength);
        var link = SafeLink(data.Link);
        var changed = Title != title || Location != location || StartsAt != data.StartsAt || EndsAt != data.EndsAt
            || AllDay != data.AllDay || Tentative != data.Tentative || Link != link;
        (Title, Location, StartsAt, EndsAt, AllDay, Tentative, Link) = (title, location, data.StartsAt, data.EndsAt, data.AllDay, data.Tentative, link);
        SyncedAt = now;
        return changed;
    }

    private static string? SafeLink(string? link) =>
        link is { Length: <= LinkMaxLength } && Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? link
            : null;

    private static string? Bounded(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        // Control characters never reach the page (titles of third parties are untrusted data).
        var clean = new string([.. trimmed.Where(character => !char.IsControl(character))]);
        return clean.Length > maxLength ? clean[..maxLength] : clean;
    }
}
