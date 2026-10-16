using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Realtime;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Engagement.Public;
using Auxilia.Application.Mailbox;
using Auxilia.Application.Tests.Identity;
using Auxilia.Application.Tests.Platform;
using Auxilia.Contracts.Mailbox;
using Auxilia.Contracts.Messages.V1.Mailbox;
using Auxilia.Contracts.Realtime;
using Auxilia.Diagnostics;
using Auxilia.Domain.Mailbox;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace Auxilia.Application.Tests.Mailbox;

public sealed class SyncTests : IAsyncDisposable
{
    private static readonly Guid Me = Guid.CreateVersion7();
    private static readonly Guid SomeoneElse = Guid.CreateVersion7();

    private readonly InMemorySyncData data = new();
    private readonly FakeCalendar google = new(ConnectedAccountProvider.Google);
    private readonly RefreshingOAuthProvider oauth = new(ConnectedAccountProvider.Google);
    private readonly IMessageOutbox outbox = Substitute.For<IMessageOutbox>();
    private readonly INotificationSender notifications = Substitute.For<INotificationSender>();
    private readonly IRealtimeNotifier realtime = Substitute.For<IRealtimeNotifier>();
    private readonly IAccessGuard guard = Substitute.For<IAccessGuard>();
    private readonly IPermissionAccess permissions = Substitute.For<IPermissionAccess>();
    private readonly ICurrentUser caller = Substitute.For<ICurrentUser>();
    private readonly ITenantContext tenant = Substitute.For<ITenantContext>();
    private readonly ManualTimeProvider clock = new();
    private readonly List<object> queued = [];
    private readonly AccountSyncRequester requester;
    private readonly AccountSyncService service;
    private readonly MailboxSyncQueryService query;

    public SyncTests()
    {
        caller.ActorType.Returns(ActorType.User);
        caller.UserId.Returns(Me);
        guard.EnsureAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        permissions.HasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var info = new TenantInfo(Guid.CreateVersion7(), "acme", TenantStatus.Active, "it", "Europe/Rome");
        tenant.Current.Returns(info);
        tenant.Tenant.Returns(info);
        outbox.EnqueueAsync(Arg.Any<IOperationScope>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => queued.Add(call.ArgAt<object>(1)));
        var settings = DefaultSettings.Create();
        var policy = new ConnectedAccountPolicy(caller);
        var calendar = new CalendarSynchronizer(ManagerHarness.Runner(), data, [google], settings, clock, NullLogger<CalendarSynchronizer>.Instance);
        requester = new AccountSyncRequester(
            ManagerHarness.Runner(), data, [calendar], outbox, settings, guard, caller, policy, clock, NullLogger<AccountSyncRequester>.Instance);
        service = new AccountSyncService(
            ManagerHarness.Runner(), data, [calendar], [oauth], new ReversingProtector(), outbox, notifications, realtime, clock,
            NullLogger<AccountSyncService>.Instance);
        query = new MailboxSyncQueryService(data, permissions, policy, tenant);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => data.DisposeAsync();

    private DateTimeOffset Now => clock.GetUtcNow();

    private ConnectedAccount Account(Guid? owner = null, bool mail = false, bool calendar = true, ConnectedAccountProvider provider = ConnectedAccountProvider.Google)
    {
        var account = ConnectedAccount.ConnectOAuth(
            Guid.CreateVersion7(), owner ?? Me, provider,
            new OAuthGrantDetails($"user{data.Accounts.Count}@example.com", null, mail, calendar, "scopes", ReversingProtector.Reverse("refresh-1")),
            "blue", Now).Value;
        data.Accounts.Add(account);
        return account;
    }

    private CalendarEventData Event(string id, double startHours = 24, string title = "Riunione") =>
        new(id, title, null, Now.AddHours(startHours), Now.AddHours(startHours + 1), false, false, null);

    private static CalendarPage Page(IEnumerable<CalendarEventData> events, string? next = null, string? sync = null, IEnumerable<string>? removed = null, int malformed = 0) =>
        new([.. events], [.. removed ?? []], next, sync, malformed);

    private async Task<SyncRun> QueueAsync(ConnectedAccount account)
    {
        var run = SyncRun.Queue(Guid.CreateVersion7(), account.Id, account.UserId, SyncScope.Calendar, SyncTrigger.PageOpen, Now);
        data.Runs.Add(run);
        (await service.SyncAsync(run.Id, 1, Ct)).IsSuccess.ShouldBeTrue();
        return run;
    }

    [Fact]
    public async Task Request_QueuesARunPerCalendarAccount_AndLeavesOutOthers()
    {
        var calendar = Account();
        Account(mail: true, calendar: false);
        var broken = Account();
        broken.MarkNeedsReconnect("AUX-30020", Now);
        Account(owner: SomeoneElse);

        var response = (await requester.RequestAsync(new StartSyncRequest("Calendar", Manual: true), Ct)).Value;

        var run = response.Runs.ShouldHaveSingleItem();
        (run.AccountId, run.Scope, run.Trigger, run.Status, run.Queued).ShouldBe((calendar.Id, "calendar", "Manual", "Queued", true));
        queued.ShouldBe([new SyncConnectedAccountCommand(run.RunId, 1)]);
    }

    [Fact]
    public async Task Request_ReusesARunInProgressOrJustEnded_AndQueuesAgainAfterTheInterval()
    {
        Account();
        var first = (await requester.RequestAsync(new StartSyncRequest("calendar", false), Ct)).Value.Runs.Single();

        var again = (await requester.RequestAsync(new StartSyncRequest("calendar", false), Ct)).Value.Runs.Single();
        (again.RunId, again.Queued).ShouldBe((first.RunId, false));

        var run = data.Runs.Single();
        run.Start(Now);
        run.Succeed(Now);
        clock.Advance(TimeSpan.FromSeconds(119));
        (await requester.RequestAsync(new StartSyncRequest("calendar", false), Ct)).Value.Runs.Single().Queued.ShouldBeFalse();

        clock.Advance(TimeSpan.FromSeconds(2));
        var later = (await requester.RequestAsync(new StartSyncRequest("calendar", false), Ct)).Value.Runs.Single();
        (later.Queued, later.RunId == first.RunId).ShouldBe((true, false));
        queued.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Request_AbandonsAStaleRun_AndQueuesANewOne()
    {
        Account();
        await requester.RequestAsync(new StartSyncRequest("calendar", false), Ct);
        clock.Advance(SyncRun.StaleAfter);

        var response = (await requester.RequestAsync(new StartSyncRequest("calendar", false), Ct)).Value;

        response.Runs.Single().Queued.ShouldBeTrue();
        var stale = data.Runs.First();
        (stale.Status, stale.ErrorCode).ShouldBe((SyncRunStatus.Failed, $"AUX-{EventCodes.Mailbox.SyncAbandoned}"));
    }

    [Fact]
    public async Task Request_RefusesAnUnknownScope_AndOtherCallersForAnotherUser()
    {
        (await requester.RequestAsync(new StartSyncRequest("contacts", false), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.SyncScopeInvalid);
        (await requester.RequestAsync(new StartSyncRequest("1", false), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.SyncScopeInvalid);
        (await requester.RequestForUserAsync(SomeoneElse, SyncScope.Calendar, SyncTrigger.Scheduled, Ct)).IsFailure.ShouldBeTrue();

        caller.ActorType.Returns(ActorType.System);
        Account(owner: SomeoneElse);
        (await requester.RequestForUserAsync(SomeoneElse, SyncScope.Calendar, SyncTrigger.Scheduled, Ct)).Value.Runs.Single().Trigger.ShouldBe("Scheduled");
    }

    [Fact]
    public async Task Sync_FullSyncInChunks_SavesTheEventsAndTheToken_AndTellsTheOwner()
    {
        var account = Account();
        google.Pages.Enqueue(_ => Page([Event("e1"), Event("e2")], next: "page-2"));
        google.Pages.Enqueue(_ => Page([Event("e3")], sync: "sync-1"));

        var run = await QueueAsync(account);

        data.Events.Select(item => item.ProviderEventId).ShouldBe(["e1", "e2", "e3"], ignoreOrder: true);
        (run.Status, run.NewCount, run.Parts).ShouldBe((SyncRunStatus.Succeeded, 3, 1));
        var cursor = data.Cursors.Single();
        (cursor.SyncToken, cursor.PageToken).ShouldBe(("sync-1", (string?)null));
        google.Requests[0].ShouldSatisfyAllConditions(
            request => request.AccessToken.ShouldBe("access-1"),
            request => request.SyncToken.ShouldBeNull(),
            request => request.WindowStart.ShouldBe(new DateTimeOffset(Now.UtcDateTime.Date.AddDays(-30), TimeSpan.Zero)),
            request => request.WindowEnd.ShouldBe(new DateTimeOffset(Now.UtcDateTime.Date.AddDays(181), TimeSpan.Zero)));
        google.Requests[1].PageToken.ShouldBe("page-2");
        account.CalendarSyncedAt.ShouldBe(Now);
        await realtime.Received(1).ToUserAsync(
            Me, RealtimeEvents.AccountSyncCompleted,
            Arg.Is<AccountSyncCompletedEvent>(payload => payload.RunId == run.Id && payload.Status == "Succeeded" && payload.NewCount == 3),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sync_Incremental_AppliesChangesAndRemovals_WithoutDuplicates()
    {
        var account = Account();
        google.Pages.Enqueue(_ => Page([Event("e1"), Event("e2")], sync: "sync-1"));
        await QueueAsync(account);
        clock.Advance(TimeSpan.FromMinutes(10));
        google.Pages.Enqueue(request =>
        {
            request.SyncToken.ShouldBe("sync-1");
            return Page([Event("e1", title: "Riunione spostata"), Event("e1", title: "Riunione spostata"), Event("e4")], sync: "sync-2", removed: ["e2"]);
        });

        var run = await QueueAsync(account);

        data.Events.Select(item => (item.ProviderEventId, item.Title)).ShouldBe([("e1", "Riunione spostata"), ("e4", "Riunione")], ignoreOrder: true);
        (run.NewCount, run.UpdatedCount).ShouldBe((1, 2));
        data.Cursors.Single().SyncToken.ShouldBe("sync-2");
    }

    [Fact]
    public async Task Sync_SkipsMalformedItemsAndKeepsOnlyTheWindow()
    {
        var account = Account();
        google.Pages.Enqueue(_ => Page(
            [Event("ok"), Event("old", startHours: -24 * 40), Event("far", startHours: 24 * 200), Event("broken") with { EndsAt = Now }],
            sync: "sync-1", malformed: 2));

        var run = await QueueAsync(account);

        data.Events.Select(item => item.ProviderEventId).ShouldBe(["ok"]);
        (run.Status, run.NewCount, run.SkippedCount).ShouldBe((SyncRunStatus.Succeeded, 1, 3));
    }

    [Fact]
    public async Task Sync_AFullSyncRemovesTheEventsItDidNotSee()
    {
        var account = Account();
        google.Pages.Enqueue(_ => Page([Event("e1"), Event("e2")], sync: "sync-1"));
        await QueueAsync(account);
        clock.Advance(TimeSpan.FromHours(1));
        google.Pages.Enqueue(_ => throw new MailboxProviderException(ProviderFailure.CursorExpired, "410"));
        google.Pages.Enqueue(request =>
        {
            request.SyncToken.ShouldBeNull();
            return Page([Event("e2")], sync: "sync-new");
        });

        var run = await QueueAsync(account);

        run.Status.ShouldBe(SyncRunStatus.Succeeded);
        data.Events.Select(item => item.ProviderEventId).ShouldBe(["e2"]);
        data.Cursors.Single().SyncToken.ShouldBe("sync-new");
    }

    [Fact]
    public async Task Sync_ACursorExpiredTwiceInOnePart_IsLeftToTheBusRetries()
    {
        var account = Account();
        google.Pages.Enqueue(_ => throw new MailboxProviderException(ProviderFailure.CursorExpired, "410"));
        google.Pages.Enqueue(_ => throw new MailboxProviderException(ProviderFailure.CursorExpired, "410"));
        var run = SyncRun.Queue(Guid.CreateVersion7(), account.Id, Me, SyncScope.Calendar, SyncTrigger.PageOpen, Now);
        data.Runs.Add(run);

        await Should.ThrowAsync<MailboxProviderException>(() => service.SyncAsync(run.Id, 1, Ct));

        (run.Status, run.ErrorCode).ShouldBe((SyncRunStatus.Running, $"AUX-{EventCodes.Mailbox.SyncProviderUnavailable}"));
    }

    [Fact]
    public async Task Sync_TransientFailure_KeepsTheSavedChunks_AndTheRedeliveryGoesOnFromThem()
    {
        var account = Account();
        google.Pages.Enqueue(_ => Page([Event("e1")], next: "page-2"));
        google.Pages.Enqueue(_ => throw new MailboxProviderException(ProviderFailure.Transient, "503"));
        var run = SyncRun.Queue(Guid.CreateVersion7(), account.Id, Me, SyncScope.Calendar, SyncTrigger.PageOpen, Now);
        data.Runs.Add(run);

        await Should.ThrowAsync<MailboxProviderException>(() => service.SyncAsync(run.Id, 1, Ct));
        (run.Status, run.NewCount, data.Cursors.Single().PageToken).ShouldBe((SyncRunStatus.Running, 1, "page-2"));

        google.Pages.Enqueue(request =>
        {
            request.PageToken.ShouldBe("page-2");
            return Page([Event("e1"), Event("e2")], sync: "sync-1");
        });
        (await service.SyncAsync(run.Id, 1, Ct)).IsSuccess.ShouldBeTrue();

        (run.Status, run.ErrorCode).ShouldBe((SyncRunStatus.Succeeded, (string?)null));
        data.Events.Select(item => item.ProviderEventId).ShouldBe(["e1", "e2"], ignoreOrder: true);
    }

    [Fact]
    public async Task Sync_RefusedGrant_MarksTheAccountToReconnect_AndNotifiesTheOwnerOnce()
    {
        var account = Account();
        oauth.Refused = true;

        var run = await QueueAsync(account);

        (run.Status, run.ErrorCode).ShouldBe((SyncRunStatus.Failed, $"AUX-{EventCodes.Mailbox.SyncCredentialsRefused}"));
        (account.Status, account.LastErrorCode).ShouldBe((ConnectedAccountStatus.NeedsReconnect, $"AUX-{EventCodes.Mailbox.SyncCredentialsRefused}"));
        await notifications.Received(1).NotifyUsersAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(users => users.Single() == Me),
            Arg.Is<NotificationMessage>(message => message.Kind == NotificationKinds.MailboxReconnect && message.EntityId == account.Id
                && message.IncludeActor && message.Parameters["address"] == account.Address),
            Arg.Any<CancellationToken>());
        google.Requests.ShouldBeEmpty();

        // The next run of the same account ends at once, without asking the provider or notifying again.
        var next = await QueueAsync(account);
        next.Status.ShouldBe(SyncRunStatus.Failed);
        await notifications.Received(1).NotifyUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<NotificationMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sync_ProviderRefusingTheAccessToken_IsACredentialsFailureToo()
    {
        var account = Account();
        google.Pages.Enqueue(_ => throw new MailboxProviderException(ProviderFailure.Credentials, "401"));

        var run = await QueueAsync(account);

        (run.Status, account.Status).ShouldBe((SyncRunStatus.Failed, ConnectedAccountStatus.NeedsReconnect));
    }

    [Fact]
    public async Task Sync_UnreachableTokenEndpoint_IsRetriedByTheBus()
    {
        var account = Account();
        oauth.Unreachable = true;
        var run = SyncRun.Queue(Guid.CreateVersion7(), account.Id, Me, SyncScope.Calendar, SyncTrigger.PageOpen, Now);
        data.Runs.Add(run);

        await Should.ThrowAsync<HttpRequestException>(() => service.SyncAsync(run.Id, 1, Ct));

        (run.Status, account.Status).ShouldBe((SyncRunStatus.Running, ConnectedAccountStatus.Active));
    }

    [Fact]
    public async Task Sync_SavesARotatedRefreshToken()
    {
        var account = Account();
        oauth.RotateTo = "refresh-2";
        google.Pages.Enqueue(_ => Page([], sync: "sync-1"));

        await QueueAsync(account);

        ReversingProtector.Reverse(account.ProtectedSecret).ShouldBe("refresh-2");
    }

    [Fact]
    public async Task Sync_OutOfTimeBudget_QueuesAContinuation()
    {
        var account = Account();
        google.Pages.Enqueue(_ =>
        {
            clock.Advance(AccountSyncService.PartBudget);
            return Page([Event("e1")], next: "page-2");
        });

        var run = await QueueAsync(account);

        (run.Status, run.Parts).ShouldBe((SyncRunStatus.Running, 2));
        queued.ShouldBe([new SyncConnectedAccountCommand(run.Id, 2)]);
        await realtime.DidNotReceiveWithAnyArgs().ToUserAsync(default, default!, default!, Ct);
    }

    [Fact]
    public async Task Sync_LeavesARunThatIsNotActive_AndFailsOneOfAnAccountWithoutTheScope()
    {
        var account = Account();
        var done = SyncRun.Queue(Guid.CreateVersion7(), account.Id, Me, SyncScope.Calendar, SyncTrigger.PageOpen, Now);
        done.Fail("AUX-30022", null, Now);
        data.Runs.Add(done);
        (await service.SyncAsync(done.Id, 1, Ct)).IsSuccess.ShouldBeTrue();
        (await service.SyncAsync(Guid.CreateVersion7(), 1, Ct)).IsSuccess.ShouldBeTrue();

        var mailOnly = Account(mail: true, calendar: false);
        var run = await QueueAsync(mailOnly);

        (run.Status, run.ErrorCode).ShouldBe((SyncRunStatus.Failed, $"AUX-{EventCodes.Mailbox.ProviderNotAvailable}"));
        google.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Query_CalendarEvents_OnlyOfMyAccounts_InTheTenantTimeZone()
    {
        var mine = Account();
        var theirs = Account(owner: SomeoneElse);
        var start = new DateTimeOffset(2026, 10, 20, 8, 30, 0, TimeSpan.Zero);
        data.Events.Add(CalendarEvent.Create(Guid.CreateVersion7(), mine.Id, new CalendarEventData("e1", "Riunione", null, start, start.AddHours(1), false, false, null), Now));
        data.Events.Add(CalendarEvent.Create(
            Guid.CreateVersion7(), mine.Id,
            new CalendarEventData("e2", "Ferie", null, new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero), true, false, null), Now));
        data.Events.Add(CalendarEvent.Create(Guid.CreateVersion7(), theirs.Id, new CalendarEventData("e3", "Altro", null, start, start.AddHours(1), false, false, null), Now));

        var response = (await query.CalendarEventsAsync(new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 25), Ct)).Value;

        response.TimeZone.ShouldBe("Europe/Rome");
        response.Items.Select(item => item.Title).ShouldBe(["Riunione", "Ferie"]);
        (response.Items[0].Date, response.Items[0].Time, response.Items[0].EndTime).ShouldBe((new DateOnly(2026, 10, 20), (TimeOnly?)new TimeOnly(10, 30), (TimeOnly?)new TimeOnly(11, 30)));
        (response.Items[1].AllDay, response.Items[1].Date, response.Items[1].EndDate, response.Items[1].Time).ShouldBe((true, new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 23), (TimeOnly?)null));
        (await query.CalendarEventsAsync(new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 19), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.CalendarRangeInvalid);
        (await query.CalendarEventsAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 2), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.CalendarRangeInvalid);
    }

    [Fact]
    public async Task Query_SyncStatus_ShowsTheNewestRunOfEachOfMyAccounts()
    {
        var account = Account();
        Account(owner: SomeoneElse);
        await requester.RequestAsync(new StartSyncRequest("calendar", false), Ct);

        var status = (await query.SyncStatusAsync("calendar", Ct)).Value;

        status.Runs.Single().ShouldSatisfyAllConditions(run => run.AccountId.ShouldBe(account.Id), run => run.Status.ShouldBe("Queued"));
        (await query.SyncStatusAsync("mail", Ct)).Value.Runs.ShouldBeEmpty();
        (await query.SyncStatusAsync(null, Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.SyncScopeInvalid);
    }
}

internal sealed class FakeCalendar(ConnectedAccountProvider provider) : ICalendarProvider
{
    public ConnectedAccountProvider Provider { get; } = provider;

    public Queue<Func<CalendarPageRequest, CalendarPage>> Pages { get; } = new();

    public List<CalendarPageRequest> Requests { get; } = [];

    public Task<CalendarPage> ReadAsync(CalendarPageRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(Pages.Dequeue()(request));
    }
}

internal sealed class RefreshingOAuthProvider(ConnectedAccountProvider provider) : IOAuthProvider
{
    public ConnectedAccountProvider Provider { get; } = provider;

    public bool IsConfigured => true;

    public bool SupportsCalendar => true;

    public bool Refused { get; set; }

    public bool Unreachable { get; set; }

    public string? RotateTo { get; set; }

    public Uri AuthorizationUrl(OAuthAuthorization authorization) => throw new NotSupportedException();

    public Task<Result<OAuthTokens>> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Result<OAuthTokens>> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (Unreachable)
        {
            throw new HttpRequestException("unreachable");
        }

        refreshToken.ShouldBe("refresh-1");
        return Task.FromResult(Refused
            ? Result.Failure<OAuthTokens>(Errors.Mailbox.OAuthExchangeFailed())
            : Result.Success(new OAuthTokens("access-1", RotateTo, DateTimeOffset.UtcNow.AddHours(1), null, false, true, null, null)));
    }

    public Task<OAuthRevocation> RevokeAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class InMemorySyncData : IMailboxSyncDataFactory, IMailboxSyncData
{
    public List<ConnectedAccount> Accounts { get; } = [];

    public List<SyncRun> Runs { get; } = [];

    public List<SyncCursor> Cursors { get; } = [];

    public List<CalendarEvent> Events { get; } = [];

    public Task<IMailboxSyncData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<IMailboxSyncData>(this);

    public Task<ConnectedAccount?> FindAccountAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Accounts.SingleOrDefault(account => account.Id == id));

    public Task<IReadOnlyList<ConnectedAccount>> AccountsOfAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ConnectedAccount>>([.. Accounts.Where(account => account.UserId == userId)]);

    public Task<SyncRun?> FindRunAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Runs.SingleOrDefault(run => run.Id == id));

    public Task<IReadOnlyList<SyncRun>> LatestRunsAsync(Guid userId, SyncScope scope, CancellationToken cancellationToken)
    {
        var accounts = Accounts.Where(account => account.UserId == userId).Select(account => account.Id).ToHashSet();
        return Task.FromResult<IReadOnlyList<SyncRun>>([.. Runs
            .Where(run => run.Scope == scope && accounts.Contains(run.AccountId))
            .GroupBy(run => run.AccountId)
            .Select(runs => runs.OrderByDescending(run => run.QueuedAt).ThenByDescending(run => run.Id).First())]);
    }

    public void Add(SyncRun run) => Runs.Add(run);

    public Task<SyncCursor?> FindCursorAsync(Guid accountId, SyncScope scope, string folderKey, CancellationToken cancellationToken) =>
        Task.FromResult(Cursors.SingleOrDefault(cursor => cursor.AccountId == accountId && cursor.Scope == scope && cursor.FolderKey == folderKey));

    public void Add(SyncCursor cursor) => Cursors.Add(cursor);

    public Task<IReadOnlyDictionary<string, CalendarEvent>> EventsAsync(Guid accountId, IReadOnlyCollection<string> providerEventIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, CalendarEvent>>(
            Events.Where(item => item.AccountId == accountId && providerEventIds.Contains(item.ProviderEventId)).ToDictionary(item => item.ProviderEventId, StringComparer.Ordinal));

    public void Add(CalendarEvent calendarEvent) => Events.Add(calendarEvent);

    public Task<int> DeleteEventsAsync(Guid accountId, IReadOnlyCollection<string> providerEventIds, CancellationToken cancellationToken) =>
        Task.FromResult(Events.RemoveAll(item => item.AccountId == accountId && providerEventIds.Contains(item.ProviderEventId)));

    public Task<int> DeleteStaleEventsAsync(Guid accountId, DateTimeOffset notSyncedSince, DateTimeOffset endedBefore, CancellationToken cancellationToken) =>
        Task.FromResult(Events.RemoveAll(item => item.AccountId == accountId && (item.SyncedAt < notSyncedSince || item.EndsAt <= endedBefore)));

    public Task<IReadOnlyList<CalendarEvent>> EventsOfUserAsync(Guid userId, DateTimeOffset from, DateTimeOffset to, int maxCount, CancellationToken cancellationToken)
    {
        var accounts = Accounts.Where(account => account.UserId == userId && account.HasCalendar).Select(account => account.Id).ToHashSet();
        return Task.FromResult<IReadOnlyList<CalendarEvent>>([.. Events
            .Where(item => accounts.Contains(item.AccountId) && item.StartsAt < to && item.EndsAt > from)
            .OrderBy(item => item.StartsAt)
            .Take(maxCount)]);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
