using System.Diagnostics;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Paging;
using Auxilia.Application.Abstractions.Realtime;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Engagement.Public;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Mailbox;
using Auxilia.Contracts.Messages.V1.Mailbox;
using Auxilia.Contracts.Realtime;
using Auxilia.Diagnostics;
using Auxilia.Domain.Mailbox;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Mailbox;

/// <summary>
/// Asks for a synchronisation of the owner's accounts (N05, #106): one run per account with the scope, queued for the
/// Worker; a run in progress, or ended less than <c>mailbox.sync.minIntervalSeconds</c> ago, is returned instead of a
/// new one. Takes <em>what</em> and <em>why</em> (<see cref="SyncTrigger"/>), so a future scheduled batch calls the same
/// code with <see cref="SyncTrigger.Scheduled"/>.
/// </summary>
public interface IAccountSyncRequester
{
    /// <summary>The caller's own accounts (page opened, or "Sync now" when <see cref="StartSyncRequest.Manual"/>).</summary>
    Task<Result<SyncRequestResponse>> RequestAsync(StartSyncRequest request, CancellationToken cancellationToken);

    /// <summary>The accounts of <paramref name="userId"/>, for a System caller (a future batch, D-15: nothing calls it yet).</summary>
    Task<Result<SyncRequestResponse>> RequestForUserAsync(Guid userId, SyncScope scope, SyncTrigger trigger, CancellationToken cancellationToken);
}

/// <summary>
/// Runs one part of a sync run in the Worker (<c>SyncConnectedAccountCommand</c>): chunks of items saved with the cursor
/// in one transaction each, a continuation queued when the time budget runs out, failures classified (N05 "Errors").
/// Idempotent: a run that is not active any more is left alone, a chunk saved twice changes nothing.
/// </summary>
public interface IAccountSyncService
{
    Task<Result> SyncAsync(Guid runId, int part, CancellationToken cancellationToken);
}

/// <summary>Sync state, copied calendar events and mail headers of the caller's own accounts.</summary>
public interface IMailboxSyncQueryService
{
    /// <summary>The inbox and sent folders of the caller's accounts with mail, with their counts.</summary>
    Task<Result<IReadOnlyList<MailFolderResponse>>> MailFoldersAsync(CancellationToken cancellationToken);

    /// <summary>A page of the caller's stored mail headers, newest first.</summary>
    Task<Result<PagedResponse<MailMessageResponse>>> MailMessagesAsync(MailMessageListQuery query, CancellationToken cancellationToken);

    /// <summary>The newest run of each of the caller's accounts for the scope (<c>mail</c> or <c>calendar</c>).</summary>
    Task<Result<SyncRequestResponse>> SyncStatusAsync(string? scope, CancellationToken cancellationToken);

    /// <summary>The events of the caller's calendars in the local days [<paramref name="from"/>, <paramref name="to"/>] (at most 62).</summary>
    Task<Result<CalendarEventsResponse>> CalendarEventsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

/// <summary>Names of scopes and triggers in URLs, messages and responses.</summary>
internal static class SyncNames
{
    public static string Of(SyncScope scope) => scope.ToString().ToLowerInvariant();

    public static bool TryParse(string? name, out SyncScope scope)
    {
        scope = default;
        return !string.IsNullOrWhiteSpace(name)
            && !name.Any(char.IsDigit)
            && Enum.TryParse(name, ignoreCase: true, out scope)
            && Enum.IsDefined(scope);
    }

    public static AccountSyncResponse ToResponse(SyncRun run, bool queued) =>
        new(run.AccountId, run.Id, Of(run.Scope), run.Trigger.ToString(), run.Status.ToString(), queued, run.QueuedAt, run.EndedAt, run.ErrorCode,
            run.NewCount, run.UpdatedCount, run.SkippedCount);
}

/// <summary>Local days of the tenant time zone and their instants (same rules as the appointments, F13).</summary>
internal static class MailboxTime
{
    public static TimeZoneInfo Zone(ITenantContext tenant) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(tenant.Tenant.TimeZone, out var zone) ? zone : TimeZoneInfo.Utc;

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

/// <summary>What a chunk left: more to read now, or the sync of the scope is complete.</summary>
internal enum ChunkOutcome
{
    More,
    Done,
}

/// <summary>A part of a sync run being worked on by <see cref="AccountSyncService"/>.</summary>
internal sealed class SyncWork(SyncRun run, ConnectedAccount account, MailboxAccess access, DateTimeOffset now)
{
    public Guid RunId { get; } = run.Id;

    public Guid AccountId { get; } = account.Id;

    public ConnectedAccountProvider Provider { get; } = account.Provider;

    public SyncScope Scope { get; } = run.Scope;

    /// <summary>The OAuth access token or the IMAP sign-in of this part (never logged or stored).</summary>
    public MailboxAccess Access { get; } = access;

    /// <summary>The OAuth access token (empty for an IMAP account).</summary>
    public string AccessToken => Access.AccessToken ?? string.Empty;

    /// <summary>When the part started: the sync window is computed from it once.</summary>
    public DateTimeOffset StartedAt { get; } = now;

    /// <summary>The cursor was reset in this part already: a second expiry is a failure, not a loop.</summary>
    public bool CursorReset { get; set; }

    /// <summary>Mail: the folders still to sync in this part (read from the provider by the first chunk).</summary>
    public Queue<MailFolderData>? MailFolders { get; set; }

    /// <summary>Mail: the folder cursors reset in this part already (a second expiry is a failure, not a loop).</summary>
    public HashSet<string> ResetFolders { get; } = new(StringComparer.Ordinal);
}

/// <summary>Reads one chunk of a scope from the provider and saves it with the cursor (one per scope: calendar and mail).</summary>
internal interface IScopeSynchronizer
{
    SyncScope Scope { get; }

    bool Supports(ConnectedAccountProvider provider);

    Task<ChunkOutcome> RunChunkAsync(SyncWork work, CancellationToken cancellationToken);
}

internal sealed class AccountSyncRequester(
    IOperationRunner operations,
    IMailboxSyncDataFactory data,
    IEnumerable<IScopeSynchronizer> synchronizers,
    IMessageOutbox outbox,
    ISettingsProvider settings,
    IAccessGuard guard,
    ICurrentUser currentUser,
    ConnectedAccountPolicy policy,
    TimeProvider clock,
    ILogger<AccountSyncRequester> logger) : IAccountSyncRequester
{
    public Task<Result<SyncRequestResponse>> RequestAsync(StartSyncRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<SyncRequestResponse>(Operations.Mailbox.RequestSync, new { request.Scope }, async scope =>
        {
            var allowed = await guard.EnsureAsync(MailboxPermissions.ViewMail, cancellationToken);
            if (allowed.IsFailure || policy.Me is not { } me)
            {
                return Result.Failure<SyncRequestResponse>(allowed.Error ?? Errors.Identity.PermissionDenied());
            }

            if (!SyncNames.TryParse(request.Scope, out var syncScope))
            {
                return Errors.Mailbox.SyncScopeInvalid();
            }

            return await QueueAsync(scope, me, syncScope, request.Manual ? SyncTrigger.Manual : SyncTrigger.PageOpen, cancellationToken);
        }, cancellationToken);
    }

    public Task<Result<SyncRequestResponse>> RequestForUserAsync(Guid userId, SyncScope scope, SyncTrigger trigger, CancellationToken cancellationToken) =>
        operations.RunAsync<SyncRequestResponse>(Operations.Mailbox.RequestSync, new { UserId = userId, Scope = scope }, async operation =>
        {
            if (currentUser.ActorType != ActorType.System || !Enum.IsDefined(scope) || !Enum.IsDefined(trigger))
            {
                return Errors.Identity.PermissionDenied();
            }

            return await QueueAsync(operation, userId, scope, trigger, cancellationToken);
        }, cancellationToken);

    private async Task<Result<SyncRequestResponse>> QueueAsync(
        IOperationScope operation, Guid userId, SyncScope scope, SyncTrigger trigger, CancellationToken cancellationToken)
    {
        var minInterval = TimeSpan.FromSeconds(await settings.GetAsync(MailboxSettings.SyncMinIntervalSeconds, cancellationToken));
        await using var store = await data.OpenAsync(cancellationToken);
        var accounts = (await store.AccountsOfAsync(userId, cancellationToken))
            .Where(account => account.Status == ConnectedAccountStatus.Active
                && account.Has(scope)
                && synchronizers.Any(synchronizer => synchronizer.Scope == scope && synchronizer.Supports(account.Provider)))
            .ToArray();
        var latest = (await store.LatestRunsAsync(userId, scope, cancellationToken)).ToDictionary(run => run.AccountId);
        var now = clock.GetUtcNow();
        var runs = new List<AccountSyncResponse>(accounts.Length);
        foreach (var account in accounts)
        {
            if (latest.GetValueOrDefault(account.Id) is { } previous)
            {
                if (previous.IsStale(now))
                {
                    Log.Mailbox.SyncAbandoned(logger, previous.Id, previous.UpdatedAt);
                    previous.Fail(Errors.Mailbox.SyncAbandoned().DisplayCode, null, now);
                }
                else if (previous.IsRecent(now, minInterval))
                {
                    runs.Add(SyncNames.ToResponse(previous, queued: false));
                    continue;
                }
            }

            var run = SyncRun.Queue(Guid.CreateVersion7(), account.Id, userId, scope, trigger, now);
            store.Add(run);
            await outbox.EnqueueAsync(operation, new SyncConnectedAccountCommand(run.Id, 1), cancellationToken);
            runs.Add(SyncNames.ToResponse(run, queued: true));
        }

        await store.SaveChangesAsync(cancellationToken);
        return new SyncRequestResponse(runs);
    }
}

internal sealed class AccountSyncService(
    IOperationRunner operations,
    IMailboxSyncDataFactory data,
    IEnumerable<IScopeSynchronizer> synchronizers,
    IEnumerable<IOAuthProvider> providers,
    IConnectedAccountSecretProtector secrets,
    IHostAddressResolver resolver,
    IMessageOutbox outbox,
    INotificationSender notifications,
    IRealtimeNotifier realtime,
    TimeProvider clock,
    ILogger<AccountSyncService> logger) : IAccountSyncService
{
    /// <summary>Time a message works before the rest of the run is queued as a continuation.</summary>
    public static readonly TimeSpan PartBudget = TimeSpan.FromMinutes(2);

    public Task<Result> SyncAsync(Guid runId, int part, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Mailbox.SyncAccount, new { SyncRunId = runId, Part = part }, async _ =>
        {
            var started = await StartAsync(runId, cancellationToken);
            if (started.IsFailure)
            {
                return started;
            }

            if (started.Value is not { } startedRun)
            {
                return Result.Success();
            }

            var (run, account, synchronizer) = startedRun;
            MailboxAccess access;
            if (account.Provider == ConnectedAccountProvider.Imap)
            {
                var login = await ImapLoginAsync(run, account, cancellationToken);
                if (login.IsFailure)
                {
                    return await EndAsync(runId, login.Error!.DisplayCode, cancellationToken);
                }

                access = MailboxAccess.Password(login.Value);
            }
            else
            {
                if (providers.FirstOrDefault(provider => provider.Provider == account.Provider && provider.IsConfigured) is not { } oauth)
                {
                    return await EndAsync(runId, Errors.Mailbox.ProviderNotAvailable(ProviderNames.Of(account.Provider)).DisplayCode, cancellationToken);
                }

                Result<OAuthTokens> refreshed;
                try
                {
                    refreshed = await oauth.RefreshAsync(secrets.Unprotect(account.ProtectedSecret), cancellationToken);
                }
                catch (Exception exception) when (IsTransient(exception, cancellationToken))
                {
                    await NoteTransientAsync(run, account, exception, cancellationToken);
                    throw;
                }

                if (refreshed.IsFailure)
                {
                    return await CredentialsRefusedAsync(runId, cancellationToken);
                }

                if (refreshed.Value.RefreshToken is { Length: > 0 } rotated)
                {
                    // The provider rotated the refresh token: the new one is saved before it is relied on.
                    await SaveRotatedSecretAsync(account.Id, secrets.Protect(rotated), cancellationToken);
                }

                access = MailboxAccess.OAuth(refreshed.Value.AccessToken);
            }

            var work = new SyncWork(run, account, access, clock.GetUtcNow());
            var deadline = work.StartedAt + PartBudget;
            try
            {
                while (await synchronizer.RunChunkAsync(work, cancellationToken) == ChunkOutcome.More)
                {
                    if (clock.GetUtcNow() >= deadline)
                    {
                        return await ContinueAsync(runId, part, cancellationToken);
                    }
                }
            }
            catch (MailboxProviderException exception) when (exception.Failure == ProviderFailure.Credentials)
            {
                return await CredentialsRefusedAsync(runId, cancellationToken);
            }
            catch (Exception exception) when (exception is MailboxProviderException || IsTransient(exception, cancellationToken))
            {
                await NoteTransientAsync(run, account, exception, cancellationToken);
                throw;
            }

            return await EndAsync(runId, errorCode: null, cancellationToken);
        }, cancellationToken);

    /// <summary>
    /// The IMAP sign-in of the part: the server name is resolved again and every address checked (the DNS answer may
    /// have changed since the account was connected), the connection uses one of the checked addresses.
    /// </summary>
    private async Task<Result<ImapLogin>> ImapLoginAsync(SyncRun run, ConnectedAccount account, CancellationToken cancellationToken)
    {
        if (account is not { ImapHost: { Length: > 0 } host, ImapPort: { } port, ImapSecurity: { } security, ImapUserName: { Length: > 0 } userName })
        {
            return Errors.Mailbox.ProviderNotAvailable(ProviderNames.Of(account.Provider));
        }

        IReadOnlyList<System.Net.IPAddress> addresses;
        try
        {
            addresses = await resolver.ResolveAsync(host, cancellationToken);
            if (addresses.Count == 0)
            {
                throw new MailboxProviderException(ProviderFailure.Transient, "HostNotResolved");
            }
        }
        catch (Exception exception) when (exception is MailboxProviderException or System.Net.Sockets.SocketException || IsTransient(exception, cancellationToken))
        {
            await NoteTransientAsync(run, account, exception, cancellationToken);
            throw;
        }

        if (addresses.Any(address => !OutboundAddressPolicy.IsAllowed(address)))
        {
            Log.Security.OutboundHostRefused(logger, account.UserId, "PrivateAddress");
            return Errors.Mailbox.ImapHostRefused();
        }

        return new ImapLogin(new ImapConnectionTarget(host, addresses[0], port, security), userName, secrets.Unprotect(account.ProtectedSecret));
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    /// <summary>The run starts (or goes on, for a continuation or a redelivery); nothing to do when it is not active any more.</summary>
    private Task<Result<(SyncRun Run, ConnectedAccount Account, IScopeSynchronizer Synchronizer)?>> StartAsync(Guid runId, CancellationToken cancellationToken) =>
        operations.RunAsync<(SyncRun, ConnectedAccount, IScopeSynchronizer)?>(Operations.Mailbox.UpdateSyncRun, new { SyncRunId = runId }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindRunAsync(runId, cancellationToken) is not { IsActive: true } run)
            {
                return Result.Success<(SyncRun, ConnectedAccount, IScopeSynchronizer)?>(null);
            }

            scope.SetEntity("ConnectedAccount", run.AccountId);
            var now = clock.GetUtcNow();
            var account = await store.FindAccountAsync(run.AccountId, cancellationToken);
            var synchronizer = account is null ? null : synchronizers.FirstOrDefault(item => item.Scope == run.Scope && item.Supports(account.Provider));
            string? refusal = account switch
            {
                null => Errors.Mailbox.AccountNotFound().DisplayCode,
                { Status: not ConnectedAccountStatus.Active } => account.LastErrorCode ?? Errors.Mailbox.SyncCredentialsRefused().DisplayCode,
                _ when account.UserId != run.UserId => Errors.Mailbox.AccountNotFound().DisplayCode,
                _ when !account.Has(run.Scope) || synchronizer is null => Errors.Mailbox.ProviderNotAvailable(ProviderNames.Of(account.Provider)).DisplayCode,
                _ => null,
            };

            if (refusal is not null)
            {
                run.Fail(refusal, Activity.Current?.TraceId.ToString(), now);
                await store.SaveChangesAsync(cancellationToken);
                Completed(scope, run);
                return Result.Success<(SyncRun, ConnectedAccount, IScopeSynchronizer)?>(null);
            }

            run.Start(now);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success<(SyncRun, ConnectedAccount, IScopeSynchronizer)?>((run, account!, synchronizer!));
        }, cancellationToken);

    private Task<Result> ContinueAsync(Guid runId, int part, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Mailbox.UpdateSyncRun, new { SyncRunId = runId }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindRunAsync(runId, cancellationToken) is not { Status: SyncRunStatus.Running } run)
            {
                return Result.Success();
            }

            run.Continue(clock.GetUtcNow());
            await outbox.EnqueueAsync(scope, new SyncConnectedAccountCommand(runId, part + 1), cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    /// <summary>The run ends: succeeded (<paramref name="errorCode"/> null, the account records the sync) or failed.</summary>
    private Task<Result> EndAsync(Guid runId, string? errorCode, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Mailbox.UpdateSyncRun, new { SyncRunId = runId }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindRunAsync(runId, cancellationToken) is not { Status: SyncRunStatus.Running } run)
            {
                return Result.Success();
            }

            var now = clock.GetUtcNow();
            var account = await store.FindAccountAsync(run.AccountId, cancellationToken);
            if (errorCode is null)
            {
                run.Succeed(now);
                account?.Synced(run.Scope, now);
            }
            else
            {
                run.Fail(errorCode, Activity.Current?.TraceId.ToString(), now);
            }

            // New mail since the previous sync (what a failed run saved arrived too): one notification, never for an empty sync.
            if (run is { Scope: SyncScope.Mail, ArrivedCount: > 0 } && account is not null)
            {
                await notifications.NotifyUsersAsync(
                    [account.UserId],
                    new NotificationMessage(
                        NotificationKinds.MailboxReceived, account.Id,
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["address"] = account.Address,
                            ["count"] = run.ArrivedCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        },
                        IncludeActor: true),
                    cancellationToken);
                Log.Mailbox.MailArrived(logger, run.ArrivedCount, account.Id);
            }

            await store.SaveChangesAsync(cancellationToken);
            Completed(scope, run);
            return Result.Success();
        }, cancellationToken);

    /// <summary>The provider refused the stored grant or password: no retry, the owner reconnects the account (notified once).</summary>
    private Task<Result> CredentialsRefusedAsync(Guid runId, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Mailbox.UpdateSyncRun, new { SyncRunId = runId }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindRunAsync(runId, cancellationToken) is not { IsActive: true } run
                || await store.FindAccountAsync(run.AccountId, cancellationToken) is not { } account)
            {
                return Result.Success();
            }

            var now = clock.GetUtcNow();
            var code = Errors.Mailbox.SyncCredentialsRefused().DisplayCode;
            var wasActive = account.Status == ConnectedAccountStatus.Active;
            account.MarkNeedsReconnect(code, now);
            run.Fail(code, Activity.Current?.TraceId.ToString(), now);
            var (providerName, scopeName) = (ProviderNames.Of(account.Provider), SyncNames.Of(run.Scope));
            Log.Mailbox.SyncCredentialsRefused(logger, providerName, account.Id, scopeName);
            if (wasActive)
            {
                await notifications.NotifyUsersAsync(
                    [account.UserId],
                    new NotificationMessage(
                        NotificationKinds.MailboxReconnect, account.Id,
                        new Dictionary<string, string>(StringComparer.Ordinal) { ["address"] = account.Address },
                        IncludeActor: true),
                    cancellationToken);
            }

            await store.SaveChangesAsync(cancellationToken);
            Completed(scope, run);
            return Result.Success();
        }, cancellationToken);

    private Task<Result> SaveRotatedSecretAsync(Guid accountId, string protectedSecret, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Mailbox.UpdateSyncRun, new { ConnectedAccountId = accountId }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            (await store.FindAccountAsync(accountId, cancellationToken))?.RotateSecret(protectedSecret, clock.GetUtcNow());
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    /// <summary>The bus retries the message; the run keeps the code so the owner sees why it is late.</summary>
    private async Task NoteTransientAsync(SyncRun run, ConnectedAccount account, Exception exception, CancellationToken cancellationToken)
    {
        var (providerName, scopeName) = (ProviderNames.Of(account.Provider), SyncNames.Of(run.Scope));
        Log.Mailbox.SyncProviderUnavailable(logger, exception, providerName, scopeName, run.Id);
        await operations.RunAsync(Operations.Mailbox.UpdateSyncRun, new { SyncRunId = run.Id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindRunAsync(run.Id, cancellationToken) is { Status: SyncRunStatus.Running } current)
            {
                current.NoteTransientFailure(Errors.Mailbox.SyncProviderUnavailable().DisplayCode, Activity.Current?.TraceId.ToString(), clock.GetUtcNow());
                await store.SaveChangesAsync(cancellationToken);
            }

            return Result.Success();
        }, cancellationToken);
    }

    /// <summary>After the commit the owner's pages refresh the data of the scope (<c>AccountSyncCompleted</c>).</summary>
    private void Completed(IOperationScope scope, SyncRun run)
    {
        var payload = new AccountSyncCompletedEvent(run.AccountId, run.Id, SyncNames.Of(run.Scope), run.Status.ToString(), run.NewCount, run.ErrorCode);
        var userId = run.UserId;
        scope.OnCommitted(ct => realtime.ToUserAsync(userId, RealtimeEvents.AccountSyncCompleted, payload, ct));
    }
}

/// <summary>
/// The primary calendar of a Google or Microsoft account (N05, #101): the window
/// −<c>mailbox.calendar.pastDays</c> / +<c>mailbox.calendar.futureDays</c> days, a full sync the first time (and every
/// <see cref="FullSyncEvery"/>, so the window moves), then incremental changes from the provider's token.
/// </summary>
internal sealed class CalendarSynchronizer(
    IOperationRunner operations,
    IMailboxSyncDataFactory data,
    IEnumerable<ICalendarProvider> calendars,
    ISettingsProvider settings,
    TimeProvider clock,
    ILogger<CalendarSynchronizer> logger) : IScopeSynchronizer
{
    public const int PageSize = 250;

    /// <summary>A full sync this old is done again: events entering the window and leaving it are caught up.</summary>
    public static readonly TimeSpan FullSyncEvery = TimeSpan.FromDays(7);

    public SyncScope Scope => SyncScope.Calendar;

    public bool Supports(ConnectedAccountProvider provider) => calendars.Any(calendar => calendar.Provider == provider);

    public async Task<ChunkOutcome> RunChunkAsync(SyncWork work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        var calendar = calendars.First(item => item.Provider == work.Provider);
        var windowStart = work.StartedAt.UtcDateTime.Date.AddDays(-await settings.GetAsync(MailboxSettings.CalendarPastDays, cancellationToken));
        var windowEnd = work.StartedAt.UtcDateTime.Date.AddDays(await settings.GetAsync(MailboxSettings.CalendarFutureDays, cancellationToken) + 1);
        var window = (Start: new DateTimeOffset(windowStart, TimeSpan.Zero), End: new DateTimeOffset(windowEnd, TimeSpan.Zero));

        SyncCursor? cursor;
        await using (var store = await data.OpenAsync(cancellationToken))
        {
            cursor = await store.FindCursorAsync(work.AccountId, Scope, SyncCursor.Primary, cancellationToken);
        }

        if (cursor is { IsFullSync: false, PageToken: null } && work.StartedAt - cursor.FullSyncStartedAt >= FullSyncEvery)
        {
            await ResetAsync(work, "window", cancellationToken);
            return ChunkOutcome.More;
        }

        CalendarPage page;
        try
        {
            page = await calendar.ReadAsync(
                new CalendarPageRequest(work.AccessToken, cursor?.PageToken, cursor?.SyncToken, window.Start, window.End, PageSize), cancellationToken);
        }
        catch (MailboxProviderException exception) when (exception.Failure == ProviderFailure.CursorExpired && !work.CursorReset)
        {
            await ResetAsync(work, exception.Reason, cancellationToken);
            return ChunkOutcome.More;
        }

        var saved = await operations.RunAsync<ChunkOutcome>(Operations.Mailbox.SaveSyncChunk, new { SyncRunId = work.RunId, ConnectedAccountId = work.AccountId }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var run = await store.FindRunAsync(work.RunId, cancellationToken);
            if (run is not { Status: SyncRunStatus.Running })
            {
                // Disconnected meanwhile (cascade) or ended elsewhere: nothing is saved.
                return ChunkOutcome.Done;
            }

            var now = clock.GetUtcNow();
            var current = await store.FindCursorAsync(work.AccountId, Scope, SyncCursor.Primary, cancellationToken);
            if (current is null)
            {
                current = SyncCursor.Begin(work.AccountId, Scope, SyncCursor.Primary, now);
                store.Add(current);
            }

            var inWindow = page.Events
                .Where(item => item.EndsAt > window.Start && item.StartsAt < window.End)
                .GroupBy(item => item.ProviderEventId, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToArray();
            var usable = inWindow.Where(CalendarEvent.IsUsable).ToArray();
            var outside = page.Events.Where(item => !inWindow.Contains(item)).Select(item => item.ProviderEventId).ToArray();
            var skipped = page.Malformed + (inWindow.Length - usable.Length);
            var existing = await store.EventsAsync(work.AccountId, [.. usable.Select(item => item.ProviderEventId)], cancellationToken);
            var (added, updated) = (0, 0);
            foreach (var item in usable)
            {
                if (existing.TryGetValue(item.ProviderEventId, out var stored))
                {
                    updated += stored.Apply(item, now) ? 1 : 0;
                }
                else
                {
                    store.Add(CalendarEvent.Create(Guid.CreateVersion7(), work.AccountId, item, now));
                    added++;
                }
            }

            var removed = page.RemovedIds.Concat(outside).Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToArray();
            if (removed.Length > 0)
            {
                updated += await store.DeleteEventsAsync(work.AccountId, removed, cancellationToken);
            }

            run.Record(added, updated, skipped, now);
            if (skipped > 0)
            {
                var scopeName = SyncNames.Of(Scope);
                Log.Mailbox.SyncItemsSkipped(logger, skipped, scopeName, work.RunId);
            }

            ChunkOutcome outcome;
            if (!string.IsNullOrEmpty(page.NextPageToken))
            {
                current.Advance(page.NextPageToken, now);
                outcome = ChunkOutcome.More;
            }
            else
            {
                if (current.IsFullSync)
                {
                    // The full sync saw every event of the window: those it did not see are gone.
                    run.Record(0, await store.DeleteStaleEventsAsync(work.AccountId, current.FullSyncStartedAt, window.Start, cancellationToken), 0, now);
                }

                if (string.IsNullOrEmpty(page.SyncToken))
                {
                    current.Reset(now);
                }
                else
                {
                    current.Complete(page.SyncToken, now);
                }

                outcome = ChunkOutcome.Done;
            }

            await store.SaveChangesAsync(cancellationToken);
            return outcome;
        }, cancellationToken);

        return saved.IsSuccess ? saved.Value : throw new InvalidOperationException(saved.Error!.Description);
    }

    private async Task ResetAsync(SyncWork work, string reason, CancellationToken cancellationToken)
    {
        work.CursorReset = true;
        var scopeName = SyncNames.Of(Scope);
        Log.Mailbox.SyncCursorReset(logger, scopeName, work.AccountId, reason);
        await operations.RunAsync(Operations.Mailbox.SaveSyncChunk, new { SyncRunId = work.RunId, ConnectedAccountId = work.AccountId }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            (await store.FindCursorAsync(work.AccountId, Scope, SyncCursor.Primary, cancellationToken))?.Reset(clock.GetUtcNow());
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
    }
}

internal sealed class MailboxSyncQueryService(
    IMailboxSyncDataFactory data, IPermissionAccess permissions, ConnectedAccountPolicy policy, ITenantContext tenant) : IMailboxSyncQueryService
{
    public const int MaxDays = 62;
    public const int MaxEvents = 2000;

    public async Task<Result<SyncRequestResponse>> SyncStatusAsync(string? scope, CancellationToken cancellationToken)
    {
        if (!await permissions.HasAsync(MailboxPermissions.ViewMail, cancellationToken) || policy.Me is not { } me)
        {
            return Errors.Identity.PermissionDenied();
        }

        if (!SyncNames.TryParse(scope, out var syncScope))
        {
            return Errors.Mailbox.SyncScopeInvalid();
        }

        await using var store = await data.OpenAsync(cancellationToken);
        return new SyncRequestResponse([.. (await store.LatestRunsAsync(me, syncScope, cancellationToken)).Select(run => SyncNames.ToResponse(run, queued: false))]);
    }

    public async Task<Result<CalendarEventsResponse>> CalendarEventsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (!await permissions.HasAsync(MailboxPermissions.ViewMail, cancellationToken) || policy.Me is not { } me)
        {
            return Errors.Identity.PermissionDenied();
        }

        if (to < from || to.DayNumber - from.DayNumber >= MaxDays)
        {
            return Errors.Mailbox.CalendarRangeInvalid();
        }

        var zone = MailboxTime.Zone(tenant);
        await using var store = await data.OpenAsync(cancellationToken);
        var events = await store.EventsOfUserAsync(me, MailboxTime.StartOf(from, zone), MailboxTime.StartOf(to.AddDays(1), zone), MaxEvents, cancellationToken);
        return new CalendarEventsResponse(zone.Id, [.. events.Select(item => ToResponse(item, zone))]);
    }

    public async Task<Result<IReadOnlyList<MailFolderResponse>>> MailFoldersAsync(CancellationToken cancellationToken)
    {
        if (!await permissions.HasAsync(MailboxPermissions.ViewMail, cancellationToken) || policy.Me is not { } me)
        {
            return Errors.Identity.PermissionDenied();
        }

        await using var store = await data.OpenAsync(cancellationToken);
        return Result.Success<IReadOnlyList<MailFolderResponse>>([.. (await store.FoldersOfUserAsync(me, cancellationToken))
            .Select(item => new MailFolderResponse(item.Folder.Id, item.Folder.AccountId, item.Folder.Kind.ToString(), item.Folder.Name, item.Total, item.Unread))]);
    }

    public async Task<Result<PagedResponse<MailMessageResponse>>> MailMessagesAsync(MailMessageListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await permissions.HasAsync(MailboxPermissions.ViewMail, cancellationToken) || policy.Me is not { } me)
        {
            return Errors.Identity.PermissionDenied();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (query.Page < 1)
        {
            errors["page"] = ["validation.paging.page"];
        }

        if (query.PageSize is < 1 or > PageRequest.MaxPageSize)
        {
            errors["pageSize"] = ["validation.paging.pageSize"];
        }

        var search = query.Search?.Trim();
        if (search is { Length: > PageRequest.MaxSearchLength })
        {
            errors["search"] = ["validation.paging.search"];
        }

        if (errors.Count > 0)
        {
            return Errors.Mailbox.MailListInvalid(errors);
        }

        await using var store = await data.OpenAsync(cancellationToken);
        var filter = new MailMessageFilter(query.AccountId, query.FolderId, string.IsNullOrEmpty(search) ? null : search, query.Unread == true);
        var (items, total) = await store.MessagesOfUserAsync(me, filter, (query.Page - 1) * query.PageSize, query.PageSize, cancellationToken);
        return new PagedResponse<MailMessageResponse>([.. items.Select(ToResponse)], query.Page, query.PageSize, total);
    }

    private static MailMessageResponse ToResponse(MailMessage item) =>
        new(item.Id, item.AccountId, item.FolderId, item.FromAddress, item.FromName, item.To, item.Cc, item.Subject, item.SentAt, item.ReceivedAt,
            item.IsRead, item.Size, item.HasAttachments, item.AttachmentNames);

    private static CalendarEventResponse ToResponse(CalendarEvent item, TimeZoneInfo zone)
    {
        if (item.AllDay)
        {
            return new CalendarEventResponse(
                item.Id, item.AccountId, item.Title, item.Location, item.StartsAt, item.EndsAt,
                DateOnly.FromDateTime(item.StartsAt.UtcDateTime), null, DateOnly.FromDateTime(item.EndsAt.UtcDateTime), null, true, item.Tentative, item.Link);
        }

        var (date, time) = MailboxTime.Local(item.StartsAt, zone);
        var (endDate, endTime) = MailboxTime.Local(item.EndsAt, zone);
        return new CalendarEventResponse(item.Id, item.AccountId, item.Title, item.Location, item.StartsAt, item.EndsAt, date, time, endDate, endTime, false, item.Tentative, item.Link);
    }
}
