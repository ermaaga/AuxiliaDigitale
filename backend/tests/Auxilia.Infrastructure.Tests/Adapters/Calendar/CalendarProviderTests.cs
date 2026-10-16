using System.Net;
using System.Text;
using System.Web;

using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Infrastructure.Adapters.Calendar;

using NSubstitute;

namespace Auxilia.Infrastructure.Tests.Adapters.Calendar;

/// <summary>Google Calendar and Microsoft Graph adapters against recorded answers (ADR 0021: no SDK, so the formats are tested here).</summary>
public sealed class CalendarProviderTests : IDisposable
{
    private static readonly DateTimeOffset WindowStart = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = new(2027, 4, 15, 0, 0, 0, TimeSpan.Zero);

    private readonly StubHandler handler = new();
    private readonly GoogleCalendarProvider google;
    private readonly MicrosoftCalendarProvider microsoft;

    public CalendarProviderTests()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(CalendarProviderBase.HttpClientName).Returns(_ => new HttpClient(handler, disposeHandler: false));
        google = new GoogleCalendarProvider(factory);
        microsoft = new MicrosoftCalendarProvider(factory);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => handler.Dispose();

    private static CalendarPageRequest Request(string? page = null, string? sync = null) => new("access-1", page, sync, WindowStart, WindowEnd, 250);

    [Fact]
    public async Task Google_FullSync_AsksTheWindow_AndReadsTimedAllDayCancelledAndMalformedEvents()
    {
        handler.Answer(HttpStatusCode.OK, """
            {
              "items": [
                { "id": "a", "status": "confirmed", "summary": "Riunione", "location": "Studio", "htmlLink": "https://www.google.com/calendar/event?eid=a",
                  "start": { "dateTime": "2026-10-20T10:30:00+02:00" }, "end": { "dateTime": "2026-10-20T11:30:00+02:00" } },
                { "id": "b", "status": "tentative", "summary": "Ferie", "start": { "date": "2026-10-21" }, "end": { "date": "2026-10-23" } },
                { "id": "c", "status": "cancelled" },
                { "id": "d", "summary": "Senza orari" },
                { "summary": "Senza id", "start": { "date": "2026-10-21" }, "end": { "date": "2026-10-22" } }
              ],
              "nextPageToken": "page-2"
            }
            """);

        var page = await google.ReadAsync(Request(), Ct);

        var query = HttpUtility.ParseQueryString(handler.LastRequest!.RequestUri!.Query);
        handler.LastRequest.RequestUri!.GetLeftPart(UriPartial.Path).ShouldBe(GoogleCalendarProvider.EventsEndpoint.ToString());
        (query["timeMin"], query["timeMax"], query["singleEvents"], query["maxResults"], query["syncToken"])
            .ShouldBe(("2026-09-16T00:00:00Z", "2027-04-15T00:00:00Z", "true", "250", (string?)null));
        handler.LastRequest.Headers.Authorization!.ToString().ShouldBe("Bearer access-1");
        page.Events.Count.ShouldBe(2);
        page.Events[0].ShouldSatisfyAllConditions(
            item => (item.ProviderEventId, item.Title, item.Location, item.AllDay, item.Tentative).ShouldBe(("a", "Riunione", "Studio", false, false)),
            item => item.StartsAt.ShouldBe(new DateTimeOffset(2026, 10, 20, 8, 30, 0, TimeSpan.Zero)),
            item => item.Link.ShouldBe("https://www.google.com/calendar/event?eid=a"));
        page.Events[1].ShouldSatisfyAllConditions(
            item => (item.AllDay, item.Tentative).ShouldBe((true, true)),
            item => (item.StartsAt, item.EndsAt).ShouldBe((new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero))));
        (page.RemovedIds.Single(), page.Malformed, page.NextPageToken, page.SyncToken).ShouldBe(("c", 2, "page-2", (string?)null));
    }

    [Fact]
    public async Task Google_Incremental_SendsTheSyncTokenAndPage_AndReturnsTheNextToken()
    {
        handler.Answer(HttpStatusCode.OK, """{ "items": [], "nextSyncToken": "sync-2" }""");

        var page = await google.ReadAsync(Request(page: "page 2", sync: "sync-1"), Ct);

        var query = HttpUtility.ParseQueryString(handler.LastRequest!.RequestUri!.Query);
        (query["syncToken"], query["pageToken"], query["timeMin"]).ShouldBe(("sync-1", "page 2", (string?)null));
        page.SyncToken.ShouldBe("sync-2");
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone, "{}", ProviderFailure.CursorExpired)]
    [InlineData(HttpStatusCode.Unauthorized, "{}", ProviderFailure.Credentials)]
    [InlineData(HttpStatusCode.Forbidden, """{ "error": { "errors": [ { "reason": "insufficientPermissions" } ] } }""", ProviderFailure.Credentials)]
    [InlineData(HttpStatusCode.Forbidden, """{ "error": { "errors": [ { "reason": "rateLimitExceeded" } ] } }""", ProviderFailure.Transient)]
    [InlineData(HttpStatusCode.TooManyRequests, "{}", ProviderFailure.Transient)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "not json", ProviderFailure.Transient)]
    public async Task Google_ClassifiesRefusals(HttpStatusCode status, string body, ProviderFailure expected)
    {
        handler.Answer(status, body);

        var failure = await Should.ThrowAsync<MailboxProviderException>(() => google.ReadAsync(Request(sync: "sync-1"), Ct));

        failure.Failure.ShouldBe(expected);
        failure.Reason.ShouldStartWith(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Google_UnreachableIsTransient()
    {
        handler.Throw = new HttpRequestException("down");

        (await Should.ThrowAsync<MailboxProviderException>(() => google.ReadAsync(Request(), Ct))).Failure.ShouldBe(ProviderFailure.Transient);
    }

    [Fact]
    public async Task Microsoft_FullSync_AsksTheWindowInUtc_AndReadsEventsRemovalsAndLinks()
    {
        handler.Answer(HttpStatusCode.OK, """
            {
              "value": [
                { "id": "AAA=", "subject": "Riunione", "isAllDay": false, "showAs": "tentative", "location": { "displayName": "Teams" },
                  "webLink": "https://outlook.live.com/owa/?itemid=AAA", "start": { "dateTime": "2026-10-20T08:30:00.0000000", "timeZone": "UTC" },
                  "end": { "dateTime": "2026-10-20T09:30:00.0000000", "timeZone": "UTC" } },
                { "id": "BBB=", "subject": "Ferie", "isAllDay": true, "start": { "dateTime": "2026-10-21T00:00:00.0000000", "timeZone": "UTC" },
                  "end": { "dateTime": "2026-10-23T00:00:00.0000000", "timeZone": "UTC" } },
                { "id": "CCC=", "@removed": { "reason": "deleted" } },
                { "id": "DDD=", "isCancelled": true },
                { "id": "EEE=", "subject": "Altro fuso", "start": { "dateTime": "2026-10-20T08:30:00", "timeZone": "Pacific Standard Time" },
                  "end": { "dateTime": "2026-10-20T09:30:00", "timeZone": "Pacific Standard Time" } }
              ],
              "@odata.nextLink": "https://graph.microsoft.com/v1.0/me/calendarView/delta?$skiptoken=abc"
            }
            """);

        var page = await microsoft.ReadAsync(Request(), Ct);

        var request = handler.LastRequest!;
        request.RequestUri!.GetLeftPart(UriPartial.Path).ShouldBe(MicrosoftCalendarProvider.DeltaEndpoint.ToString());
        var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
        (query["startDateTime"], query["endDateTime"]).ShouldBe(("2026-09-16T00:00:00Z", "2027-04-15T00:00:00Z"));
        string.Join(", ", request.Headers.GetValues("Prefer")).ShouldContain("outlook.timezone=\"UTC\"");
        page.Events.Select(item => item.ProviderEventId).ShouldBe(["AAA=", "BBB="]);
        page.Events[0].ShouldSatisfyAllConditions(
            item => (item.Title, item.Location, item.Tentative, item.AllDay).ShouldBe(("Riunione", "Teams", true, false)),
            item => item.StartsAt.ShouldBe(new DateTimeOffset(2026, 10, 20, 8, 30, 0, TimeSpan.Zero)));
        page.Events[1].EndsAt.ShouldBe(new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero));
        page.RemovedIds.ShouldBe(["CCC=", "DDD="]);
        (page.Malformed, page.NextPageToken, page.SyncToken).ShouldBe((1, "https://graph.microsoft.com/v1.0/me/calendarView/delta?$skiptoken=abc", (string?)null));
    }

    [Fact]
    public async Task Microsoft_FollowsOnlyGraphLinks()
    {
        handler.Answer(HttpStatusCode.OK, """{ "value": [], "@odata.deltaLink": "https://evil.example/delta" }""");
        var page = await microsoft.ReadAsync(Request(sync: "https://graph.microsoft.com/v1.0/me/calendarView/delta?$deltatoken=x"), Ct);

        handler.LastRequest!.RequestUri!.ToString().ShouldBe("https://graph.microsoft.com/v1.0/me/calendarView/delta?$deltatoken=x");
        page.SyncToken.ShouldBeNull();
        (await Should.ThrowAsync<MailboxProviderException>(() => microsoft.ReadAsync(Request(page: "https://evil.example/next"), Ct)))
            .Failure.ShouldBe(ProviderFailure.CursorExpired);
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone, ProviderFailure.CursorExpired)]
    [InlineData(HttpStatusCode.Unauthorized, ProviderFailure.Credentials)]
    [InlineData(HttpStatusCode.TooManyRequests, ProviderFailure.Transient)]
    public async Task Microsoft_ClassifiesRefusals(HttpStatusCode status, ProviderFailure expected)
    {
        handler.Answer(status, """{ "error": { "code": "syncStateNotFound", "message": "mario@example.com" } }""");

        var failure = await Should.ThrowAsync<MailboxProviderException>(() => microsoft.ReadAsync(Request(sync: "https://graph.microsoft.com/v1.0/x"), Ct));

        failure.Failure.ShouldBe(expected);
        failure.Reason.ShouldBe($"{(int)status} syncStateNotFound");
        failure.Message.ShouldNotContain("mario");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private HttpStatusCode status = HttpStatusCode.OK;
        private string body = "{}";

        public HttpRequestMessage? LastRequest { get; private set; }

        public Exception? Throw { get; set; }

        public void Answer(HttpStatusCode answerStatus, string answerBody) => (status, body, Throw) = (answerStatus, answerBody, null);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Throw is not null
                ? throw Throw
                : Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
