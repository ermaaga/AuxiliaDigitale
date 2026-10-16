namespace Auxilia.Contracts.Mailbox;

/// <summary>Synchronise the caller's accounts (N05, #106): <c>scope</c> <c>mail</c> or <c>calendar</c>; <c>manual</c> for "Sync now" (otherwise a page was opened).</summary>
public sealed record StartSyncRequest(string Scope, bool Manual);

/// <summary>The runs of the caller's accounts that have the scope (accounts to reconnect are left out).</summary>
public sealed record SyncRequestResponse(IReadOnlyList<AccountSyncResponse> Runs);

/// <summary>
/// The sync of one account and scope. <c>queued</c>: false when a run in progress (or ended less than the minimum
/// interval ago) was returned instead of a new one.
/// </summary>
/// <param name="Status"><c>Queued</c>, <c>Running</c>, <c>Succeeded</c> or <c>Failed</c>.</param>
/// <param name="ErrorCode"><c>AUX-…</c> code of the failure, or of the last transient error while the run is retried.</param>
public sealed record AccountSyncResponse(
    Guid AccountId,
    Guid RunId,
    string Scope,
    string Trigger,
    string Status,
    bool Queued,
    DateTimeOffset QueuedAt,
    DateTimeOffset? EndedAt,
    string? ErrorCode,
    int NewCount,
    int UpdatedCount,
    int SkippedCount);

/// <summary>The events of the caller's calendars in a range of local days, with the tenant time zone the dates are in.</summary>
public sealed record CalendarEventsResponse(string TimeZone, IReadOnlyList<CalendarEventResponse> Items);

/// <summary>
/// An event of a connected calendar (read only). <c>startsAt</c>/<c>endsAt</c> are instants (end exclusive); <c>date</c>,
/// <c>time</c>, <c>endDate</c>, <c>endTime</c> the same in the tenant time zone; all-day events have no times and their
/// dates are the provider's (end exclusive).
/// </summary>
public sealed record CalendarEventResponse(
    Guid Id,
    Guid AccountId,
    string Title,
    string? Location,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateOnly Date,
    TimeOnly? Time,
    DateOnly EndDate,
    TimeOnly? EndTime,
    bool AllDay,
    bool Tentative,
    string? Link);
