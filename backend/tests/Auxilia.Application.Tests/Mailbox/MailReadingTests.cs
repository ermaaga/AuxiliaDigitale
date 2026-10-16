using System.Text;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Application.Cases.Public;
using Auxilia.Application.Documents.Public;
using Auxilia.Application.Mailbox;
using Auxilia.Application.Tests.Identity;
using Auxilia.Application.Tests.Platform;
using Auxilia.Contracts.Mailbox;
using Auxilia.Diagnostics;
using Auxilia.Domain.Mailbox;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace Auxilia.Application.Tests.Mailbox;

/// <summary>C-06b: a message read live, its .eml and attachments, filed into a case (fake provider, MIME reader and documents).</summary>
public sealed class MailReadingTests : IAsyncDisposable
{
    private static readonly Guid Me = Guid.CreateVersion7();
    private static readonly Guid SomeoneElse = Guid.CreateVersion7();
    private static readonly byte[] Original = Encoding.UTF8.GetBytes("From: mario@example.com\r\nSubject: Documenti\r\n\r\nCiao");

    private readonly InMemorySyncData data = new();
    private readonly FakeMail gmail = new(ConnectedAccountProvider.Google);
    private readonly RefreshingOAuthProvider oauth = new(ConnectedAccountProvider.Google);
    private readonly FakeContentReader reader = new();
    private readonly FakeCaseDocuments documents = new();
    private readonly ICaseDirectory cases = Substitute.For<ICaseDirectory>();
    private readonly IAccessGuard guard = Substitute.For<IAccessGuard>();
    private readonly IPermissionAccess permissions = Substitute.For<IPermissionAccess>();
    private readonly ICurrentUser caller = Substitute.For<ICurrentUser>();
    private readonly ManualTimeProvider clock = new();
    private readonly Guid caseId = Guid.CreateVersion7();
    private readonly MailMessageQueryService query;
    private readonly MailMessageManager manager;

    public MailReadingTests()
    {
        caller.ActorType.Returns(ActorType.User);
        caller.UserId.Returns(Me);
        guard.EnsureAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        permissions.HasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        cases.FindAsync(caseId, Arg.Any<CancellationToken>()).Returns(new CaseSummary(caseId, "2026-00015", Guid.CreateVersion7(), Guid.CreateVersion7(), "Dichiarazione", true, true));
        var policy = new ConnectedAccountPolicy(caller);
        var resolver = new MailboxAccessResolver(
            ManagerHarness.Runner(), data, [oauth], new ReversingProtector(), Substitute.For<IHostAddressResolver>(), clock, NullLogger<MailboxAccessResolver>.Instance);
        var live = new LiveMessages(ManagerHarness.Runner(), data, [gmail], resolver, permissions, policy, clock, NullLogger<LiveMessages>.Instance);
        query = new MailMessageQueryService(live, reader, data, cases);
        manager = new MailMessageManager(ManagerHarness.Runner(), live, reader, data, documents, guard, policy, clock);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => data.DisposeAsync();

    private MailMessage Message(Guid? owner = null, string providerId = "m1", string? messageId = "<abc@example.com>", string subject = "Documenti: pratica 15/2026?")
    {
        var account = ConnectedAccount.ConnectOAuth(
            Guid.CreateVersion7(), owner ?? Me, ConnectedAccountProvider.Google,
            new OAuthGrantDetails($"user{data.Accounts.Count}@gmail.com", null, true, false, "scopes", ReversingProtector.Reverse("refresh-1")),
            "blue", clock.GetUtcNow()).Value;
        data.Accounts.Add(account);
        var folder = MailFolder.Create(Guid.CreateVersion7(), account.Id, new MailFolderData("INBOX", "Inbox", MailFolderKind.Inbox), clock.GetUtcNow());
        data.Folders.Add(folder);
        var message = MailMessage.Create(Guid.CreateVersion7(), account.Id, folder.Id,
            new MailMessageData(providerId, messageId, null, "mario@example.com", "Mario", [], [], subject, null,
                new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero), false, Original.Length, true, ["fattura.pdf"]),
            clock.GetUtcNow());
        data.Messages.Add(message);
        gmail.Raw[providerId] = Original;
        return message;
    }

    [Fact]
    public async Task Read_AsksTheProviderLive_AndReturnsBodiesAttachmentsAndTheVisibleFilings()
    {
        var message = Message();
        var hidden = Guid.CreateVersion7();
        cases.FindAsync(hidden, Arg.Any<CancellationToken>()).Returns(new CaseSummary(hidden, "2026-00016", Guid.CreateVersion7(), Guid.CreateVersion7(), "x", false, false));
        var key = LiveMessages.Hash("<abc@example.com>");
        data.Filed.Add(FiledMessage.Record(Guid.CreateVersion7(), key, LiveMessages.Hash("a"), caseId, Guid.CreateVersion7(), Me, clock.GetUtcNow()));
        data.Filed.Add(FiledMessage.Record(Guid.CreateVersion7(), key, LiveMessages.Hash("a"), hidden, Guid.CreateVersion7(), SomeoneElse, clock.GetUtcNow()));

        var detail = (await query.ReadAsync(message.Id, Ct)).Value;

        (detail.Text, detail.Html, detail.Truncated).ShouldBe(("Ciao", "<p>Ciao</p>", false));
        detail.Message.Id.ShouldBe(message.Id);
        detail.Attachments.ShouldHaveSingleItem().ShouldBe(new MailAttachmentResponse(0, "fattura.pdf", "application/pdf", 4));
        detail.FiledIn.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            filed => filed.CaseId.ShouldBe(caseId), filed => filed.CaseNumber.ShouldBe("2026-00015"));
        var request = gmail.RawRequests.ShouldHaveSingleItem();
        (request.Access.AccessToken, request.Folder.ProviderFolderId, request.Id).ShouldBe(("access-1", "INBOX", "m1"));
        reader.MaxBodyChars.ShouldBe(MailRaw.MaxBodyChars);
    }

    [Fact]
    public async Task Read_RefusesOtherUsersMessages_AndMessagesGoneAtTheProvider()
    {
        var theirs = Message(owner: SomeoneElse, providerId: "x1");
        var gone = Message(providerId: "gone");
        gmail.Raw.Remove("gone");

        (await query.ReadAsync(theirs.Id, Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.MailMessageNotFound);
        (await query.ReadAsync(gone.Id, Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.MailMessageNotFound);
        (await query.ReadAsync(Guid.CreateVersion7(), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.MailMessageNotFound);
        gmail.RawRequests.Single().Id.ShouldBe("gone");

        permissions.HasAsync(MailboxPermissions.ViewMail, Arg.Any<CancellationToken>()).Returns(false);
        (await query.ReadAsync(gone.Id, Ct)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Read_TooLargeMessage_IsRefused()
    {
        var message = Message();
        gmail.Raw["m1"] = new byte[MailRaw.MaxBytes + 1];

        (await query.ReadAsync(message.Id, Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.MailMessageTooLarge);
    }

    [Fact]
    public async Task Read_ProviderFailures_BecomeCodes_AndARefusedGrantMarksTheAccount()
    {
        var message = Message();
        gmail.RawFailure = new MailboxProviderException(ProviderFailure.Transient, "503");
        (await query.ReadAsync(message.Id, Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.SyncProviderUnavailable);

        gmail.RawFailure = new MailboxProviderException(ProviderFailure.Credentials, "401");
        (await query.ReadAsync(message.Id, Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.SyncCredentialsRefused);
        var account = data.Accounts.Single();
        (account.Status, account.LastErrorCode).ShouldBe((ConnectedAccountStatus.NeedsReconnect, $"AUX-{EventCodes.Mailbox.SyncCredentialsRefused}"));

        // The account to reconnect is not asked again.
        gmail.RawFailure = null;
        (await query.ReadAsync(message.Id, Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.SyncCredentialsRefused);
        gmail.RawRequests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Read_RefusedRefreshToken_MarksTheAccountToo()
    {
        var message = Message();
        oauth.Refused = true;

        (await query.ReadAsync(message.Id, Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.SyncCredentialsRefused);

        data.Accounts.Single().Status.ShouldBe(ConnectedAccountStatus.NeedsReconnect);
        gmail.RawRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Eml_IsTheOriginal_NamedAfterDayAndSubject()
    {
        var message = Message();

        var file = (await query.OpenEmlAsync(message.Id, Ct)).Value;

        await using (file.Content)
        {
            ((MemoryStream)file.Content).ToArray().ShouldBe(Original);
        }

        (file.FileName, file.ContentType).ShouldBe(("2026-10-16 Documenti pratica 15 2026.eml", "message/rfc822"));
        LiveMessages.EmlFileName(Message(providerId: "m2", subject: "  ")).ShouldBe("2026-10-16 message.eml");
    }

    [Fact]
    public async Task Attachment_IsTakenOutOfTheOriginal()
    {
        var message = Message();

        var file = (await query.OpenAttachmentAsync(message.Id, 0, Ct)).Value;
        (file.FileName, file.ContentType).ShouldBe(("fattura.pdf", "application/pdf"));

        (await query.OpenAttachmentAsync(message.Id, 3, Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.MailAttachmentNotFound);
        (await query.OpenAttachmentAsync(message.Id, -1, Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.MailAttachmentNotFound);
    }

    [Fact]
    public async Task File_StoresTheEmlAndTheChosenAttachments_AndRecordsTheFiling()
    {
        var message = Message();
        var folder = Guid.CreateVersion7();

        var filed = (await manager.FileAsync(message.Id, new FileMailRequest(caseId, folder, [0], "Dal cliente", false), Ct)).Value;

        var filing = documents.Filings.ShouldHaveSingleItem();
        (filing.CaseId, filing.FolderId, filing.Description).ShouldBe((caseId, (Guid?)folder, "Dal cliente"));
        filing.Files.Select(file => file.FileName).ShouldBe(["2026-10-16 Documenti pratica 15 2026.eml", "fattura.pdf"]);
        documents.Contents[0].ShouldBe(Original);
        filed.MessageDocumentId.ShouldBe(documents.Ids[0]);
        filed.AttachmentDocumentIds.ShouldBe([documents.Ids[1]]);
        var record = data.Filed.ShouldHaveSingleItem();
        (record.CaseId, record.DocumentId, record.UserId).ShouldBe((caseId, documents.Ids[0], Me));
        record.MessageKeyHash.ShouldBe(LiveMessages.Hash("<abc@example.com>"));
        record.AccountAddressHash.ShouldNotContain("@");
        reader.Opened.ShouldBe([0]);
    }

    [Fact]
    public async Task File_TheSameMessageTwiceIntoACase_NeedsForce_EvenFromAnotherAccount()
    {
        var message = Message();
        (await manager.FileAsync(message.Id, new FileMailRequest(caseId, null, null, null, false), Ct)).IsSuccess.ShouldBeTrue();
        var copy = Message(providerId: "copy");

        (await manager.FileAsync(copy.Id, new FileMailRequest(caseId, null, null, null, false), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.MailAlreadyFiled);
        (await manager.FileAsync(copy.Id, new FileMailRequest(caseId, null, null, null, true), Ct)).IsSuccess.ShouldBeTrue();
        data.Filed.Count.ShouldBe(2);
    }

    [Fact]
    public async Task File_RefusesBadAttachments_MissingPermission_AndPassesOnTheDocumentsRefusal()
    {
        var message = Message();

        (await manager.FileAsync(message.Id, new FileMailRequest(caseId, null, [0, 0], null, false), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.MailFilingInvalid);
        (await manager.FileAsync(message.Id, new FileMailRequest(caseId, null, [-1], null, false), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.MailFilingInvalid);
        (await manager.FileAsync(message.Id, new FileMailRequest(caseId, null, [5], null, false), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.MailFilingInvalid);
        (await manager.FileAsync(message.Id, new FileMailRequest(caseId, null, [.. Enumerable.Range(0, 50)], null, false), Ct)).Error!.Code
            .ShouldBe(EventCodes.Mailbox.MailFilingInvalid);

        documents.Refusal = Errors.Identity.PermissionDenied();
        (await manager.FileAsync(message.Id, new FileMailRequest(caseId, null, null, null, false), Ct)).Error!.Code.ShouldBe(Errors.Identity.PermissionDenied().Code);
        data.Filed.ShouldBeEmpty();

        guard.EnsureAsync(MailboxPermissions.FileMail, Arg.Any<CancellationToken>()).Returns(Result.Failure(Errors.Identity.PermissionDenied()));
        (await manager.FileAsync(message.Id, new FileMailRequest(caseId, null, null, null, false), Ct)).IsFailure.ShouldBeTrue();
        documents.Filings.ShouldBeEmpty();
    }

    [Fact]
    public async Task KeyHash_UsesTheMessageId_OrTheProviderIdWithoutOne()
    {
        var withId = Message();
        var withoutId = Message(providerId: "m9", messageId: null);
        await using var store = await data.OpenAsync(Ct);

        LiveMessages.KeyHash((await store.FindMessageOfUserAsync(Me, withId.Id, Ct))!).ShouldBe(LiveMessages.Hash("<abc@example.com>"));
        var owned = (await store.FindMessageOfUserAsync(Me, withoutId.Id, Ct))!;
        LiveMessages.KeyHash(owned).ShouldBe(LiveMessages.Hash($"{owned.Account.Id}:Inbox:m9"));
        LiveMessages.Hash("x").Length.ShouldBe(64);
    }
}

internal sealed class FakeContentReader : IMailContentReader
{
    public int MaxBodyChars { get; private set; }

    public List<int> Opened { get; } = [];

    public Task<MailContent> ReadAsync(Stream raw, int maxBodyChars, CancellationToken cancellationToken)
    {
        MaxBodyChars = maxBodyChars;
        return Task.FromResult(new MailContent("Ciao", "<p>Ciao</p>", false, [new MailAttachmentInfo(0, "fattura.pdf", "application/pdf", 4)]));
    }

    public Task<MailAttachmentContent?> OpenAttachmentAsync(Stream raw, int index, CancellationToken cancellationToken)
    {
        Opened.Add(index);
        return Task.FromResult(index == 0
            ? new MailAttachmentContent(new MemoryStream("%PDF"u8.ToArray()), new MailAttachmentInfo(0, "fattura.pdf", "application/pdf", 4))
            : null);
    }
}

internal sealed class FakeCaseDocuments : ICaseDocuments
{
    public List<CaseFiling> Filings { get; } = [];

    public List<byte[]> Contents { get; } = [];

    public List<Guid> Ids { get; } = [];

    public Error? Refusal { get; set; }

    public Task<Result<IReadOnlyList<Guid>>> FileAsync(CaseFiling filing, CancellationToken cancellationToken)
    {
        if (Refusal is not null)
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<Guid>>(Refusal));
        }

        Filings.Add(filing);
        Contents.Clear();
        Ids.Clear();
        foreach (var file in filing.Files)
        {
            using var copy = new MemoryStream();
            file.Content.CopyTo(copy);
            Contents.Add(copy.ToArray());
            Ids.Add(Guid.CreateVersion7());
        }

        return Task.FromResult(Result.Success<IReadOnlyList<Guid>>([.. Ids]));
    }
}
