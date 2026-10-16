using System.Net;

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
using Auxilia.Diagnostics;
using Auxilia.Domain.Mailbox;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace Auxilia.Application.Tests.Mailbox;

/// <summary>The mail scope of the sync engine (C-06) with fake providers: chunks, cursors, arrivals, IMAP sign-in and the list.</summary>
public sealed class MailSyncTests : IAsyncDisposable
{
    private static readonly Guid Me = Guid.CreateVersion7();
    private static readonly Guid SomeoneElse = Guid.CreateVersion7();

    private readonly InMemorySyncData data = new();
    private readonly FakeMail gmail = new(ConnectedAccountProvider.Google);
    private readonly FakeMail imap = new(ConnectedAccountProvider.Imap);
    private readonly RefreshingOAuthProvider oauth = new(ConnectedAccountProvider.Google);
    private readonly IHostAddressResolver resolver = Substitute.For<IHostAddressResolver>();
    private readonly IMessageOutbox outbox = Substitute.For<IMessageOutbox>();
    private readonly INotificationSender notifications = Substitute.For<INotificationSender>();
    private readonly IRealtimeNotifier realtime = Substitute.For<IRealtimeNotifier>();
    private readonly IAccessGuard guard = Substitute.For<IAccessGuard>();
    private readonly IPermissionAccess permissions = Substitute.For<IPermissionAccess>();
    private readonly ICurrentUser caller = Substitute.For<ICurrentUser>();
    private readonly ITenantContext tenant = Substitute.For<ITenantContext>();
    private readonly ManualTimeProvider clock = new();
    private readonly AccountSyncRequester requester;
    private readonly AccountSyncService service;
    private readonly MailboxSyncQueryService query;

    public MailSyncTests()
    {
        caller.ActorType.Returns(ActorType.User);
        caller.UserId.Returns(Me);
        guard.EnsureAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        permissions.HasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var info = new TenantInfo(Guid.CreateVersion7(), "acme", TenantStatus.Active, "it", "Europe/Rome");
        tenant.Current.Returns(info);
        tenant.Tenant.Returns(info);
        outbox.EnqueueAsync(Arg.Any<IOperationScope>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        resolver.ResolveAsync("imaps.pec.aruba.it", Arg.Any<CancellationToken>()).Returns([IPAddress.Parse("62.149.128.10")]);
        var settings = DefaultSettings.Create();
        var policy = new ConnectedAccountPolicy(caller);
        var mail = new MailSynchronizer(ManagerHarness.Runner(), data, [gmail, imap], settings, clock, NullLogger<MailSynchronizer>.Instance);
        requester = new AccountSyncRequester(ManagerHarness.Runner(), data, [mail], outbox, settings, guard, caller, policy, clock, NullLogger<AccountSyncRequester>.Instance);
        service = new AccountSyncService(
            ManagerHarness.Runner(), data, [mail], [oauth], new ReversingProtector(), resolver, outbox, notifications, realtime, clock,
            NullLogger<AccountSyncService>.Instance);
        query = new MailboxSyncQueryService(data, permissions, policy, tenant);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => data.DisposeAsync();

    private DateTimeOffset Now => clock.GetUtcNow();

    private ConnectedAccount Google(Guid? owner = null)
    {
        var account = ConnectedAccount.ConnectOAuth(
            Guid.CreateVersion7(), owner ?? Me, ConnectedAccountProvider.Google,
            new OAuthGrantDetails($"user{data.Accounts.Count}@gmail.com", null, true, false, "scopes", ReversingProtector.Reverse("refresh-1")),
            "blue", Now).Value;
        data.Accounts.Add(account);
        return account;
    }

    private ConnectedAccount Pec()
    {
        var account = ConnectedAccount.ConnectImap(
            Guid.CreateVersion7(), Me, "studio@pec.it", new ImapServer("imaps.pec.aruba.it", 993, ImapSecurity.SslOnConnect), "studio@pec.it",
            ReversingProtector.Reverse("secret"), "green", Now).Value;
        data.Accounts.Add(account);
        return account;
    }

    private MailMessageData Message(string id, bool read = false, string subject = "Pratica", double minutesAgo = 10) =>
        new(id, $"<{id}@example.com>", null, "mario@example.com", "Mario Rossi", ["studio@example.com"], [], subject,
            Now.AddMinutes(-minutesAgo), Now.AddMinutes(-minutesAgo), read, 1024, false, []);

    private static MailPage Page(IEnumerable<MailMessageData> messages, string? next = null, string? sync = null, IEnumerable<string>? removed = null,
        IReadOnlyCollection<string>? present = null, int malformed = 0) =>
        new([.. messages], [.. removed ?? []], present, next, sync, malformed);

    private async Task<SyncRun> RunAsync(ConnectedAccount account)
    {
        var run = SyncRun.Queue(Guid.CreateVersion7(), account.Id, account.UserId, SyncScope.Mail, SyncTrigger.PageOpen, Now);
        data.Runs.Add(run);
        (await service.SyncAsync(run.Id, 1, Ct)).IsSuccess.ShouldBeTrue();
        return run;
    }

    private MailFolder Folder(ConnectedAccount account, MailFolderKind kind) => data.Folders.Single(folder => folder.AccountId == account.Id && folder.Kind == kind);

    [Fact]
    public async Task FirstSync_CopiesInboxAndSentInChunks_BoundedByTheSettings_WithoutNotifying()
    {
        var account = Google();
        gmail.Inbox.Enqueue(_ => Page([Message("i1"), Message("i2")], next: "page-2"));
        gmail.Inbox.Enqueue(_ => Page([Message("i3")], sync: "h-100"));
        gmail.Sent.Enqueue(_ => Page([Message("s1", read: true)], sync: "h-100"));

        var run = await RunAsync(account);

        (run.Status, run.NewCount, run.ArrivedCount).ShouldBe((SyncRunStatus.Succeeded, 4, 0));
        data.Folders.Select(folder => (folder.Kind, folder.ProviderFolderId)).ShouldBe([(MailFolderKind.Inbox, "INBOX"), (MailFolderKind.Sent, "SENT")]);
        data.Messages.Count(item => item.FolderId == Folder(account, MailFolderKind.Inbox).Id).ShouldBe(3);
        data.Cursors.Select(cursor => (cursor.FolderKey, cursor.SyncToken)).ShouldBe([("mail:inbox", "h-100"), ("mail:sent", "h-100")], ignoreOrder: true);
        gmail.Requests[0].ShouldSatisfyAllConditions(
            request => request.Access.AccessToken.ShouldBe("access-1"),
            request => (request.SyncToken, request.MaxMessages, request.PageSize).ShouldBe(((string?)null, 2000, 100)),
            request => request.Since.ShouldBe(Now.AddDays(-90)));
        gmail.Requests[1].PageToken.ShouldBe("page-2");
        account.MailSyncedAt.ShouldBe(Now);
        await notifications.DidNotReceiveWithAnyArgs().NotifyUsersAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task IncrementalSync_AppliesChanges_AndNotifiesOnlyTheNewMail()
    {
        var account = Google();
        gmail.Inbox.Enqueue(_ => Page([Message("i1"), Message("i2")], sync: "h-1"));
        gmail.Sent.Enqueue(_ => Page([], sync: "h-1"));
        await RunAsync(account);
        clock.Advance(TimeSpan.FromMinutes(5));
        gmail.Inbox.Enqueue(request =>
        {
            request.SyncToken.ShouldBe("h-1");
            return Page([Message("i1", read: true), Message("i3", minutesAgo: 1), Message("i3", minutesAgo: 1)], sync: "h-2", removed: ["i2"]);
        });
        gmail.Sent.Enqueue(_ => Page([], sync: "h-2"));

        var run = await RunAsync(account);

        data.Messages.Select(item => (item.ProviderMessageId, item.IsRead)).ShouldBe([("i1", true), ("i3", false)], ignoreOrder: true);
        (run.NewCount, run.UpdatedCount, run.ArrivedCount, run.SkippedCount).ShouldBe((1, 2, 1, 0));
        await notifications.Received(1).NotifyUsersAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(users => users.Single() == Me),
            Arg.Is<NotificationMessage>(message => message.Kind == NotificationKinds.MailboxReceived && message.EntityId == account.Id
                && message.Parameters["count"] == "1" && message.Parameters["address"] == account.Address),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExpiredCursor_RunsABoundedFullSync_ThatRemovesWhatItDidNotSee()
    {
        var account = Google();
        gmail.Inbox.Enqueue(_ => Page([Message("i1"), Message("i2")], sync: "h-1"));
        gmail.Sent.Enqueue(_ => Page([], sync: "h-1"));
        await RunAsync(account);
        clock.Advance(TimeSpan.FromHours(1));
        gmail.Inbox.Enqueue(_ => throw new MailboxProviderException(ProviderFailure.CursorExpired, "404"));
        gmail.Inbox.Enqueue(request =>
        {
            (request.SyncToken, request.PageToken).ShouldBe(((string?)null, (string?)null));
            return Page([Message("i2"), Message("i9")], sync: "h-9");
        });
        gmail.Sent.Enqueue(_ => Page([], sync: "h-9"));

        var run = await RunAsync(account);

        run.Status.ShouldBe(SyncRunStatus.Succeeded);
        data.Messages.Select(item => item.ProviderMessageId).ShouldBe(["i2", "i9"], ignoreOrder: true);
        run.ArrivedCount.ShouldBe(0);
        await notifications.DidNotReceiveWithAnyArgs().NotifyUsersAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task MalformedHeaders_AreSkippedAndCounted()
    {
        var account = Google();
        gmail.Inbox.Enqueue(_ => Page([Message("ok"), Message(" "), Message("big") with { Size = -5 }], sync: "h-1", malformed: 2));
        gmail.Sent.Enqueue(_ => Page([], sync: "h-1"));

        var run = await RunAsync(account);

        (run.NewCount, run.SkippedCount).ShouldBe((1, 4));
        data.Messages.Single().ProviderMessageId.ShouldBe("ok");
    }

    [Fact]
    public async Task Imap_SignsInWithThePassword_OnTheCheckedAddress_AndRemovesWhatTheFolderNoLongerHolds()
    {
        var account = Pec();
        imap.SentFolder = false;
        imap.Inbox.Enqueue(_ => Page([Message("7:1"), Message("7:2")], sync: "v1|7|2"));
        await RunAsync(account);
        clock.Advance(TimeSpan.FromMinutes(5));
        imap.Inbox.Enqueue(_ => Page([Message("7:3")], sync: "v1|7|3", present: ["7:2", "7:3"]));

        var run = await RunAsync(account);

        var login = imap.Requests[0].Access.Imap.ShouldNotBeNull();
        (login.Target.Host, login.Target.Address, login.Target.Port, login.UserName, login.Password)
            .ShouldBe(("imaps.pec.aruba.it", IPAddress.Parse("62.149.128.10"), 993, "studio@pec.it", "secret"));
        imap.Requests[0].Access.ToString().ShouldNotContain("secret");
        login.ToString().ShouldNotContain("secret");
        data.Folders.ShouldHaveSingleItem().Kind.ShouldBe(MailFolderKind.Inbox);
        data.Messages.Select(item => item.ProviderMessageId).ShouldBe(["7:2", "7:3"], ignoreOrder: true);
        (run.ArrivedCount, run.UpdatedCount).ShouldBe((1, 1));
    }

    [Fact]
    public async Task Imap_ServerNowOnAPrivateAddress_FailsTheRunWithoutConnecting()
    {
        var account = Pec();
        resolver.ResolveAsync("imaps.pec.aruba.it", Arg.Any<CancellationToken>()).Returns([IPAddress.Parse("62.149.128.10"), IPAddress.Parse("10.0.0.5")]);

        var run = await RunAsync(account);

        (run.Status, run.ErrorCode).ShouldBe((SyncRunStatus.Failed, $"AUX-{EventCodes.Mailbox.ImapHostRefused}"));
        account.Status.ShouldBe(ConnectedAccountStatus.Active);
        imap.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Imap_UnresolvedServer_IsRetriedByTheBus()
    {
        var account = Pec();
        resolver.ResolveAsync("imaps.pec.aruba.it", Arg.Any<CancellationToken>()).Returns([]);
        var run = SyncRun.Queue(Guid.CreateVersion7(), account.Id, Me, SyncScope.Mail, SyncTrigger.PageOpen, Now);
        data.Runs.Add(run);

        await Should.ThrowAsync<MailboxProviderException>(() => service.SyncAsync(run.Id, 1, Ct));

        (run.Status, run.ErrorCode).ShouldBe((SyncRunStatus.Running, $"AUX-{EventCodes.Mailbox.SyncProviderUnavailable}"));
    }

    [Fact]
    public async Task Imap_PasswordRefused_MarksTheAccountToReconnect()
    {
        var account = Pec();
        imap.FoldersFailure = new MailboxProviderException(ProviderFailure.Credentials, "AuthenticationFailed");

        var run = await RunAsync(account);

        (run.Status, account.Status).ShouldBe((SyncRunStatus.Failed, ConnectedAccountStatus.NeedsReconnect));
        await notifications.Received(1).NotifyUsersAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Is<NotificationMessage>(message => message.Kind == NotificationKinds.MailboxReconnect), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Request_QueuesMailRunsForGoogleAndImapAccounts()
    {
        var google = Google();
        var pec = Pec();

        var runs = (await requester.RequestAsync(new StartSyncRequest("mail", false), Ct)).Value.Runs;

        runs.Select(run => run.AccountId).ShouldBe([google.Id, pec.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task Query_ListsOnlyMyFoldersAndMessages_NewestFirst_WithSearchAndPaging()
    {
        var mine = Google();
        var theirs = Google(owner: SomeoneElse);
        gmail.Inbox.Enqueue(_ => Page([Message("i1", subject: "Fattura marzo", minutesAgo: 30), Message("i2", subject: "Appuntamento", minutesAgo: 5),
            Message("i3", read: true, subject: "Fattura aprile", minutesAgo: 1)], sync: "h-1"));
        gmail.Sent.Enqueue(_ => Page([], sync: "h-1"));
        await RunAsync(mine);
        gmail.Inbox.Enqueue(_ => Page([Message("x1", subject: "Fattura altrui")], sync: "h-1"));
        gmail.Sent.Enqueue(_ => Page([], sync: "h-1"));
        await RunAsync(theirs);

        var folders = (await query.MailFoldersAsync(Ct)).Value;
        folders.Select(folder => (folder.AccountId, folder.Kind, folder.Total, folder.Unread))
            .ShouldBe([(mine.Id, "Inbox", 3, 2), (mine.Id, "Sent", 0, 0)]);

        var all = (await query.MailMessagesAsync(new MailMessageListQuery(null, null, null, null, 1, 2), Ct)).Value;
        all.TotalCount.ShouldBe(3);
        all.Items.Select(item => item.Subject).ShouldBe(["Fattura aprile", "Appuntamento"]);

        var invoices = (await query.MailMessagesAsync(new MailMessageListQuery(mine.Id, folders[0].Id, " fattura ", true, 1, 25), Ct)).Value;
        invoices.Items.Select(item => item.Subject).ShouldBe(["Fattura marzo"]);
        invoices.Items[0].ShouldSatisfyAllConditions(
            item => item.FromAddress.ShouldBe("mario@example.com"),
            item => item.To.ShouldBe(["studio@example.com"]));

        var invalid = await query.MailMessagesAsync(new MailMessageListQuery(null, null, new string('a', 201), null, 0, 101), Ct);
        invalid.Error!.Code.ShouldBe(EventCodes.Mailbox.MailListInvalid);
        invalid.Error.ValidationErrors.Keys.ShouldBe(["page", "pageSize", "search"], ignoreOrder: true);

        permissions.HasAsync(MailboxPermissions.ViewMail, Arg.Any<CancellationToken>()).Returns(false);
        (await query.MailFoldersAsync(Ct)).IsFailure.ShouldBeTrue();
    }
}

internal sealed class FakeMail(ConnectedAccountProvider provider) : IMailProvider
{
    public ConnectedAccountProvider Provider { get; } = provider;

    public Queue<Func<MailPageRequest, MailPage>> Inbox { get; } = new();

    public Queue<Func<MailPageRequest, MailPage>> Sent { get; } = new();

    public bool SentFolder { get; set; } = true;

    public MailboxProviderException? FoldersFailure { get; set; }

    public List<MailPageRequest> Requests { get; } = [];

    public Task<IReadOnlyList<MailFolderData>> FoldersAsync(MailboxAccess access, CancellationToken cancellationToken)
    {
        if (FoldersFailure is not null)
        {
            throw FoldersFailure;
        }

        IReadOnlyList<MailFolderData> folders = SentFolder
            ? [new("SENT", "Sent", MailFolderKind.Sent), new("INBOX", "Inbox", MailFolderKind.Inbox)]
            : [new("INBOX", "Inbox", MailFolderKind.Inbox)];
        return Task.FromResult(folders);
    }

    public Task<MailPage> ReadAsync(MailPageRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var pages = request.Folder.Kind == MailFolderKind.Inbox ? Inbox : Sent;
        return Task.FromResult(pages.Dequeue()(request));
    }
}
