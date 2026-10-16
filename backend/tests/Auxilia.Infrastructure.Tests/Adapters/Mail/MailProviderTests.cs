using System.Net;
using System.Text;
using System.Web;

using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Domain.Mailbox;
using Auxilia.Infrastructure.Adapters.Calendar;
using Auxilia.Infrastructure.Adapters.Mail;
using Auxilia.Infrastructure.Adapters.Mail.Imap;

using NSubstitute;

namespace Auxilia.Infrastructure.Tests.Adapters.Mail;

/// <summary>Gmail and Microsoft Graph mail adapters against recorded answers, IMAP cursors (ADR 0021: no SDK, so the formats are tested here).</summary>
public sealed class MailProviderTests : IDisposable
{
    private static readonly DateTimeOffset Since = new(2026, 7, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly MailFolderData GmailInbox = new("INBOX", "Inbox", MailFolderKind.Inbox);
    private static readonly MailFolderData GraphInbox = new("inbox", "Inbox", MailFolderKind.Inbox);

    private readonly RoutedHandler handler = new();
    private readonly GmailMailProvider gmail;
    private readonly GraphMailProvider graph;

    public MailProviderTests()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(CalendarProviderBase.HttpClientName).Returns(_ => new HttpClient(handler, disposeHandler: false));
        gmail = new GmailMailProvider(factory);
        graph = new GraphMailProvider(factory);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => handler.Dispose();

    private static MailPageRequest Request(MailFolderData folder, string? page = null, string? sync = null, int max = 2000) =>
        new(MailboxAccess.OAuth("access-1"), folder, page, sync, Since, max, 100);

    private const string GmailMessage = """
        {
          "id": "m1", "threadId": "t1", "labelIds": ["INBOX", "UNREAD"], "sizeEstimate": 48213, "internalDate": "1760605200000",
          "payload": {
            "headers": [
              { "name": "From", "value": "\"Rossi, Mario\" <Mario.Rossi@example.com>" },
              { "name": "To", "value": "Studio <studio@example.com>, altro@example.com" },
              { "name": "Subject", "value": "Documenti pratica" },
              { "name": "Date", "value": "Thu, 16 Oct 2026 10:59:58 +0200" },
              { "name": "Message-ID", "value": "<abc@example.com>" }
            ],
            "parts": [ { "filename": "" }, { "filename": "fattura.pdf" }, { "filename": "", "parts": [ { "filename": "foto.jpg" } ] } ]
          }
        }
        """;

    [Fact]
    public async Task Gmail_FullSync_NotesTheHistoryId_ListsTheNewestAfterTheWindow_AndReadsHeadersOnly()
    {
        handler.On("/profile", """{ "emailAddress": "mario@gmail.com", "historyId": "9001" }""");
        handler.On("/messages?", """{ "messages": [ { "id": "m1" }, { "id": "gone" } ], "nextPageToken": "list-2" }""");
        handler.On("/messages/m1", GmailMessage);
        handler.On("/messages/gone", """{ "error": { "code": 404 } }""", HttpStatusCode.NotFound);

        var page = await gmail.ReadAsync(Request(GmailInbox), Ct);

        var list = handler.Requests.Single(request => request.RequestUri!.AbsolutePath.EndsWith("/messages", StringComparison.Ordinal));
        var query = HttpUtility.ParseQueryString(list.RequestUri!.Query);
        (query["labelIds"], query["maxResults"], query["q"]).ShouldBe(("INBOX", "100", $"after:{Since.ToUnixTimeSeconds()}"));
        var get = HttpUtility.ParseQueryString(handler.Requests.First(request => request.RequestUri!.AbsolutePath.EndsWith("/messages/m1", StringComparison.Ordinal)).RequestUri!.Query);
        (get["format"], get["fields"]).ShouldBe(("full", GmailMailProvider.MessageFields));
        handler.Requests.ShouldAllBe(request => request.Headers.Authorization!.ToString() == "Bearer access-1");

        var message = page.Messages.ShouldHaveSingleItem();
        (message.ProviderMessageId, message.ThreadId, message.InternetMessageId).ShouldBe(("m1", "t1", "<abc@example.com>"));
        (message.FromAddress, message.FromName, message.Subject).ShouldBe(("Mario.Rossi@example.com", "Rossi, Mario", "Documenti pratica"));
        message.To.ShouldBe(["Studio <studio@example.com>", "altro@example.com"]);
        (message.IsRead, message.Size, message.HasAttachments).ShouldBe((false, 48213L, true));
        message.AttachmentNames.ShouldBe(["fattura.pdf", "foto.jpg"]);
        message.ReceivedAt.ShouldBe(DateTimeOffset.FromUnixTimeMilliseconds(1760605200000));
        message.SentAt.ShouldBe(new DateTimeOffset(2026, 10, 16, 8, 59, 58, TimeSpan.Zero));
        (page.RemovedIds.Single(), page.NextPageToken, page.SyncToken).ShouldBe(("gone", "g1|9001|2|list-2", (string?)null));
    }

    [Fact]
    public async Task Gmail_FullSync_StopsAtTheMaximum_AndEndsWithTheHistoryId()
    {
        handler.On("/messages?", """{ "messages": [ { "id": "m1" } ], "nextPageToken": "list-3" }""");
        handler.On("/messages/m1", GmailMessage);

        var page = await gmail.ReadAsync(Request(GmailInbox, page: "g1|9001|1999|list-2"), Ct);

        var query = HttpUtility.ParseQueryString(handler.Requests[0].RequestUri!.Query);
        (query["pageToken"], query["maxResults"]).ShouldBe(("list-2", "1"));
        (page.NextPageToken, page.SyncToken).ShouldBe(((string?)null, "9001"));
        handler.Requests.ShouldNotContain(request => request.RequestUri!.AbsolutePath.EndsWith("/profile", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Gmail_History_ReadsAddedAndChanged_RemovesDeletedAndMovedOut()
    {
        handler.On("/history?", """
            {
              "history": [
                { "messagesAdded": [ { "message": { "id": "new", "labelIds": ["INBOX", "UNREAD"] } }, { "message": { "id": "elsewhere", "labelIds": ["SENT"] } } ] },
                { "labelsRemoved": [ { "message": { "id": "read", "labelIds": ["INBOX"] }, "labelIds": ["UNREAD"] } ] },
                { "labelsRemoved": [ { "message": { "id": "archived", "labelIds": [] }, "labelIds": ["INBOX"] } ] },
                { "messagesDeleted": [ { "message": { "id": "deleted" } } ] }
              ],
              "historyId": "9100"
            }
            """);
        handler.On("/messages/new", GmailMessage.Replace("\"m1\"", "\"new\"", StringComparison.Ordinal));
        handler.On("/messages/read", GmailMessage.Replace("\"m1\"", "\"read\"", StringComparison.Ordinal).Replace(", \"UNREAD\"", "", StringComparison.Ordinal));

        var page = await gmail.ReadAsync(Request(GmailInbox, sync: "9001"), Ct);

        var query = HttpUtility.ParseQueryString(handler.Requests[0].RequestUri!.Query);
        (query["startHistoryId"], query["labelId"]).ShouldBe(("9001", "INBOX"));
        query.GetValues("historyTypes").ShouldBe(["messageAdded", "messageDeleted", "labelAdded", "labelRemoved"]);
        page.Messages.Select(message => (message.ProviderMessageId, message.IsRead)).ShouldBe([("new", false), ("read", true)]);
        page.RemovedIds.ShouldBe(["archived", "deleted"], ignoreOrder: true);
        page.SyncToken.ShouldBe("9100");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ProviderFailure.CursorExpired)]
    [InlineData(HttpStatusCode.Unauthorized, ProviderFailure.Credentials)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ProviderFailure.Transient)]
    public async Task Gmail_History_ClassifiesTheFailures(HttpStatusCode status, ProviderFailure expected)
    {
        handler.On("/history?", """{ "error": { "code": 404, "message": "mario@gmail.com not found", "errors": [ { "reason": "notFound" } ] } }""", status);

        var failure = await Should.ThrowAsync<MailboxProviderException>(() => gmail.ReadAsync(Request(GmailInbox, sync: "1"), Ct));

        failure.Failure.ShouldBe(expected);
        failure.Message.ShouldNotContain("mario");
    }

    [Fact]
    public async Task Graph_FullSync_AsksTheWindowNewestFirst_AndBoundsWhatItStores()
    {
        handler.On("/messages/delta", """
            {
              "value": [
                { "id": "b", "subject": "Senza data" },
                { "id": "a", "internetMessageId": "<a@example.com>", "conversationId": "c1", "subject": "Preventivo",
                  "from": { "emailAddress": { "name": "Mario Rossi", "address": "mario@example.com" } },
                  "toRecipients": [ { "emailAddress": { "name": "Studio", "address": "studio@example.com" } } ],
                  "ccRecipients": [], "sentDateTime": "2026-10-16T08:00:00Z", "receivedDateTime": "2026-10-16T08:00:05Z",
                  "isRead": true, "hasAttachments": true },
                { "id": "c", "@removed": { "reason": "deleted" } },
                { "id": "d", "subject": "Oltre il limite", "receivedDateTime": "2026-10-15T08:00:05Z" }
              ],
              "@odata.nextLink": "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$skiptoken=2"
            }
            """);

        var page = await graph.ReadAsync(Request(GraphInbox, max: 1), Ct);

        var uri = handler.Requests[0].RequestUri!;
        uri.AbsolutePath.ShouldBe("/v1.0/me/mailFolders/inbox/messages/delta");
        var query = HttpUtility.ParseQueryString(uri.Query);
        (query["$select"], query["$filter"], query["$orderby"]).ShouldBe((GraphMailProvider.Select, "receivedDateTime ge 2026-07-18T09:00:00Z", "receivedDateTime desc"));
        handler.Requests[0].Headers.GetValues("Prefer").ShouldContain("odata.maxpagesize=100");
        var message = page.Messages.ShouldHaveSingleItem();
        (message.ProviderMessageId, message.FromAddress, message.IsRead, message.HasAttachments).ShouldBe(("a", "mario@example.com", true, true));
        message.To.ShouldBe(["Studio <studio@example.com>"]);
        (page.RemovedIds.Single(), page.Malformed).ShouldBe(("c", 1));
        page.NextPageToken.ShouldBe("m1|1|https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$skiptoken=2");
    }

    [Fact]
    public async Task Graph_FollowsOnlyGraphLinks_AndEndsWithTheDeltaLink()
    {
        handler.On("/messages/delta", """{ "value": [], "@odata.deltaLink": "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$deltatoken=x" }""");

        var page = await graph.ReadAsync(Request(GraphInbox, page: "m1|5|https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$skiptoken=2"), Ct);

        handler.Requests[0].RequestUri!.Query.ShouldContain("skiptoken=2");
        (page.NextPageToken, page.SyncToken).ShouldBe(((string?)null, "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$deltatoken=x"));
        (await Should.ThrowAsync<MailboxProviderException>(() => graph.ReadAsync(Request(GraphInbox, sync: "https://evil.example.com/delta"), Ct)))
            .Failure.ShouldBe(ProviderFailure.CursorExpired);
    }

    [Fact]
    public async Task Providers_OfferTheInboxAndTheSentMail()
    {
        (await gmail.FoldersAsync(MailboxAccess.OAuth("x"), Ct)).Select(folder => (folder.ProviderFolderId, folder.Kind))
            .ShouldBe([("INBOX", MailFolderKind.Inbox), ("SENT", MailFolderKind.Sent)]);
        (await graph.FoldersAsync(MailboxAccess.OAuth("x"), Ct)).Select(folder => (folder.ProviderFolderId, folder.Kind))
            .ShouldBe([("inbox", MailFolderKind.Inbox), ("sentitems", MailFolderKind.Sent)]);
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public void ImapCursor_RoundTripsItsTokens_AndPagesTheUids()
    {
        ImapCursor.Parse("v1|7|120").ShouldBe(ImapCursor.Synced(7, 120));
        ImapCursor.Parse("f|7|100|200|150").ShouldBe(ImapCursor.Full(7, 100, 200, 150));
        ImapCursor.Parse("i|7|130").ShouldBe(ImapCursor.Incremental(7, 130));
        ImapCursor.Full(7, 100, 200, 150).ToString().ShouldBe("f|7|100|200|150");
        ImapCursor.Synced(7, 120).ToString().ShouldBe("v1|7|120");
        ImapCursor.Parse("g1|9001|2|x").ShouldBeNull();
        ImapCursor.Parse("v1|-7|1").ShouldBeNull();
        ImapCursor.Parse(null).ShouldBeNull();
        ImapCursor.MessageId(7, 42).ShouldBe("7:42");

        uint[] uids = [5, 1, 9, 3, 7];
        ImapCursor.FullPage(uids, below: 0, take: 2).ShouldBe([9u, 7u]);
        ImapCursor.FullPage(uids, below: 7, take: 10).ShouldBe([5u, 3u, 1u]);
        ImapCursor.IncrementalPage(uids, after: 3, take: 2).ShouldBe([5u, 7u]);
        ImapCursor.IncrementalPage(uids, after: 9, take: 2).ShouldBeEmpty();
    }

    [Fact]
    public void MailHeaders_ReadAddressesAndDates()
    {
        MailHeaders.First("=?UTF-8?B?TWFyaW8gUm9zc2k=?= <mario@example.com>").ShouldBe(("mario@example.com", "Mario Rossi"));
        MailHeaders.First("not an address").ShouldBe(((string?)null, (string?)null));
        MailHeaders.Recipients("a@example.com, Bea <b@example.com>").ShouldBe(["a@example.com", "Bea <b@example.com>"]);
        MailHeaders.Date("garbage").ShouldBeNull();
    }

    [Fact]
    public async Task Gmail_Raw_DecodesTheBase64UrlMessage_AndIsNullWhenGone()
    {
        var original = Encoding.UTF8.GetBytes("From: mario@example.com\r\nSubject: Ciao ??\r\n\r\nTesto con caratteri >>> e ???");
        var raw = Convert.ToBase64String(original).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        handler.On("/messages/m1?format=raw", $$"""{ "raw": "{{raw}}" }""");
        handler.On("/messages/gone?format=raw", """{ "error": { "code": 404 } }""", HttpStatusCode.NotFound);

        await using (var stream = await gmail.OpenRawAsync(MailboxAccess.OAuth("access-1"), GmailInbox, "m1", Ct))
        {
            using var copy = new MemoryStream();
            await stream!.CopyToAsync(copy, Ct);
            copy.ToArray().ShouldBe(original);
        }

        HttpUtility.ParseQueryString(handler.Requests[0].RequestUri!.Query)["fields"].ShouldBe("raw");
        (await gmail.OpenRawAsync(MailboxAccess.OAuth("access-1"), GmailInbox, "gone", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Gmail_Raw_LargerThanTheBound_IsCut()
    {
        var big = new byte[100_000];
        Random.Shared.NextBytes(big);
        var raw = Convert.ToBase64String(big).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        await using var stream = await GmailMailProvider.DecodeRawAsync(Encoding.UTF8.GetBytes($$"""{"raw":"{{raw}}"}"""), Ct);

        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, Ct);
        copy.ToArray().ShouldBe(big);
        await Should.ThrowAsync<MailboxProviderException>(() => GmailMailProvider.DecodeRawAsync("""{"id":"x"}"""u8.ToArray(), Ct));
    }

    [Fact]
    public async Task Graph_Raw_ReadsTheValueOfTheMessage_AndClassifiesTheFailures()
    {
        handler.On("/me/messages/AAMk%2Fx%3D/$value", "From: a@example.com\r\n\r\nCiao");
        handler.On("/me/messages/denied/$value", "{}", HttpStatusCode.Unauthorized);
        handler.On("/me/messages/missing/$value", "{}", HttpStatusCode.NotFound);

        await using (var stream = await graph.OpenRawAsync(MailboxAccess.OAuth("access-1"), GraphInbox, "AAMk/x=", Ct))
        {
            using var text = new StreamReader(stream!);
            (await text.ReadToEndAsync(Ct)).ShouldStartWith("From: a@example.com");
        }

        (await graph.OpenRawAsync(MailboxAccess.OAuth("access-1"), GraphInbox, "missing", Ct)).ShouldBeNull();
        (await Should.ThrowAsync<MailboxProviderException>(() => graph.OpenRawAsync(MailboxAccess.OAuth("access-1"), GraphInbox, "denied", Ct)))
            .Failure.ShouldBe(ProviderFailure.Credentials);
    }

    [Fact]
    public async Task TemporaryFiles_CutAtTheBoundPlusOne()
    {
        await using var small = await TemporaryFiles.CopyBoundedAsync(new MemoryStream(new byte[10]), 100, Ct);
        await using var large = await TemporaryFiles.CopyBoundedAsync(new MemoryStream(new byte[1000]), 100, Ct);

        (small.Length, small.Position, large.Length).ShouldBe((10L, 0L, 101L));
    }

    private const string Mime = """
        From: "Mario Rossi" <mario@example.com>
        To: studio@example.com
        Subject: Documenti
        Message-ID: <abc@example.com>
        MIME-Version: 1.0
        Content-Type: multipart/mixed; boundary="b1"

        --b1
        Content-Type: multipart/alternative; boundary="b2"

        --b2
        Content-Type: text/plain; charset=utf-8

        Buongiorno, in allegato la fattura.
        --b2
        Content-Type: text/html; charset=utf-8

        <p>Buongiorno, <img src="https://tracker.example/x.png"> in allegato la fattura.</p>
        --b2--
        --b1
        Content-Type: application/pdf; name="fattura.pdf"
        Content-Disposition: attachment; filename="../fattura.pdf"
        Content-Transfer-Encoding: base64

        JVBERi0xLjcK
        --b1
        Content-Type: message/rfc822
        Content-Disposition: attachment; filename="postacert"

        From: pec@example.com
        Subject: Inner

        Messaggio interno
        --b1--
        """;

    [Fact]
    public async Task MimeReader_ReadsBodiesAndAttachments_AndTakesOneOut()
    {
        var reader = new MimeMailContentReader();
        await using var raw = TemporaryFiles.Create();
        await raw.WriteAsync(Encoding.UTF8.GetBytes(Mime.ReplaceLineEndings("\r\n")), Ct);

        var content = await reader.ReadAsync(raw, 1000, Ct);

        content.Text!.Trim().ShouldBe("Buongiorno, in allegato la fattura.");
        content.Html!.ShouldContain("<p>Buongiorno");
        content.Truncated.ShouldBeFalse();
        content.Attachments.Select(item => (item.Index, item.FileName, item.ContentType)).ShouldBe(
            [(0, "_fattura.pdf", "application/pdf"), (1, "postacert.eml", "message/rfc822")]);
        content.Attachments[0].Size.ShouldBe(9);

        var pdf = (await reader.OpenAttachmentAsync(raw, 0, Ct)).ShouldNotBeNull();
        await using (pdf.Content)
        {
            using var copy = new MemoryStream();
            await pdf.Content.CopyToAsync(copy, Ct);
            copy.ToArray().ShouldBe("%PDF-1.7\n"u8.ToArray());
        }

        var inner = (await reader.OpenAttachmentAsync(raw, 1, Ct)).ShouldNotBeNull();
        await using (inner.Content)
        {
            using var text = new StreamReader(inner.Content);
            (await text.ReadToEndAsync(Ct)).ShouldContain("Messaggio interno");
        }

        (await reader.OpenAttachmentAsync(raw, 2, Ct)).ShouldBeNull();
        (await reader.ReadAsync(raw, 10, Ct)).Truncated.ShouldBeTrue();
    }

    [Fact]
    public async Task MimeReader_ToleratesWhatIsNotAMessage()
    {
        var reader = new MimeMailContentReader();
        await using var raw = new MemoryStream("not a mime message at all"u8.ToArray());

        var content = await reader.ReadAsync(raw, 1000, Ct);

        content.Attachments.ShouldBeEmpty();
        MimeMailContentReader.SafeFileName("a/b:c?.pdf", 0, message: false).ShouldBe("a_b_c_.pdf");
        MimeMailContentReader.SafeFileName(" .. ", 2, message: true).ShouldBe("message-3.eml");
        MimeMailContentReader.SafeFileName(null, 0, message: false).ShouldBe("attachment-1");
    }

    private sealed class RoutedHandler : HttpMessageHandler
    {
        private readonly List<(string Fragment, HttpStatusCode Status, string Body)> routes = [];

        public List<HttpRequestMessage> Requests { get; } = [];

        public void On(string fragment, string body, HttpStatusCode status = HttpStatusCode.OK) => routes.Add((fragment, status, body));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var target = request.RequestUri!.PathAndQuery;
            var route = routes.LastOrDefault(item => target.Contains(item.Fragment, StringComparison.Ordinal));
            return Task.FromResult(route.Fragment is null
                ? new HttpResponseMessage(HttpStatusCode.NotImplemented) { Content = new StringContent("{}", Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(route.Status) { Content = new StringContent(route.Body, Encoding.UTF8, "application/json") });
        }
    }
}
