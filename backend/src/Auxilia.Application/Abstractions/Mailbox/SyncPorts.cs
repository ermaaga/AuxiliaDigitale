using Auxilia.Domain.Mailbox;

namespace Auxilia.Application.Abstractions.Mailbox;

/// <summary>Sync runs, cursors and copied calendar events of the current tenant (<c>mailbox</c> schema, ADR 0021).</summary>
public interface IMailboxSyncData : IAsyncDisposable
{
    /// <summary>Any user's account (the Worker works on the account of a run); null when it was disconnected.</summary>
    Task<ConnectedAccount?> FindAccountAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConnectedAccount>> AccountsOfAsync(Guid userId, CancellationToken cancellationToken);

    Task<SyncRun?> FindRunAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The newest run of each of the user's accounts for the scope (by queue time).</summary>
    Task<IReadOnlyList<SyncRun>> LatestRunsAsync(Guid userId, SyncScope scope, CancellationToken cancellationToken);

    void Add(SyncRun run);

    Task<SyncCursor?> FindCursorAsync(Guid accountId, SyncScope scope, string folderKey, CancellationToken cancellationToken);

    void Add(SyncCursor cursor);

    /// <summary>The stored events of the account among <paramref name="providerEventIds"/>, by provider event id.</summary>
    Task<IReadOnlyDictionary<string, CalendarEvent>> EventsAsync(Guid accountId, IReadOnlyCollection<string> providerEventIds, CancellationToken cancellationToken);

    void Add(CalendarEvent calendarEvent);

    Task<int> DeleteEventsAsync(Guid accountId, IReadOnlyCollection<string> providerEventIds, CancellationToken cancellationToken);

    /// <summary>After a full sync: the events it did not see (synced before it started), and those ended before the window.</summary>
    Task<int> DeleteStaleEventsAsync(Guid accountId, DateTimeOffset notSyncedSince, DateTimeOffset endedBefore, CancellationToken cancellationToken);

    /// <summary>The events of the user's own accounts overlapping [<paramref name="from"/>, <paramref name="to"/>), by start.</summary>
    Task<IReadOnlyList<CalendarEvent>> EventsOfUserAsync(Guid userId, DateTimeOffset from, DateTimeOffset to, int maxCount, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IMailboxSyncDataFactory
{
    Task<IMailboxSyncData> OpenAsync(CancellationToken cancellationToken);
}

/// <summary>How a provider call failed, which decides what the sync does (N05 "Errors").</summary>
public enum ProviderFailure
{
    /// <summary>Grant revoked or password changed: no retry, the account needs to be reconnected.</summary>
    Credentials,

    /// <summary>Timeout, network, 429/5xx after the HTTP retries: the bus retries the message later.</summary>
    Transient,

    /// <summary>The sync token is no longer valid (Google 410, Graph 410, UIDVALIDITY changed): bounded full sync.</summary>
    CursorExpired,
}

/// <summary>A classified failure of a mail or calendar provider (adapters throw it; nothing else is expected).</summary>
public sealed class MailboxProviderException : Exception
{
    public MailboxProviderException(ProviderFailure failure, string reason, Exception? innerException = null)
        : base($"{failure}: {reason}", innerException)
    {
        Failure = failure;
        Reason = reason;
    }

    public MailboxProviderException()
        : this(ProviderFailure.Transient, "unknown")
    {
    }

    public MailboxProviderException(string message)
        : this(ProviderFailure.Transient, message)
    {
    }

    public MailboxProviderException(string message, Exception innerException)
        : this(ProviderFailure.Transient, message, innerException)
    {
    }

    public ProviderFailure Failure { get; }

    /// <summary>A short technical reason for the log (HTTP status, provider error code): never data of the account.</summary>
    public string Reason { get; }
}

/// <summary>
/// One page of a calendar read: from <paramref name="PageToken"/> when a sync is in progress, otherwise from
/// <paramref name="SyncToken"/> (incremental) or the window (full sync, <paramref name="SyncToken"/> null).
/// </summary>
public sealed record CalendarPageRequest(
    string AccessToken, string? PageToken, string? SyncToken, DateTimeOffset WindowStart, DateTimeOffset WindowEnd, int PageSize);

/// <param name="Events">New or changed events (malformed items are left out and counted in <paramref name="Malformed"/>).</param>
/// <param name="RemovedIds">Events cancelled or deleted at the provider.</param>
/// <param name="NextPageToken">More pages follow; null on the last page.</param>
/// <param name="SyncToken">On the last page: the token of the next incremental sync.</param>
public sealed record CalendarPage(
    IReadOnlyList<CalendarEventData> Events, IReadOnlyList<string> RemovedIds, string? NextPageToken, string? SyncToken, int Malformed);

/// <summary>The primary calendar of a Google or Microsoft account (read only, ADR 0021); throws <see cref="MailboxProviderException"/>.</summary>
public interface ICalendarProvider
{
    ConnectedAccountProvider Provider { get; }

    Task<CalendarPage> ReadAsync(CalendarPageRequest request, CancellationToken cancellationToken);
}
