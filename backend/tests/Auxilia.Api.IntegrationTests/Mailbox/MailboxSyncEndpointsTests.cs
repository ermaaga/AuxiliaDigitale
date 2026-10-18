using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Contracts.Mailbox;
using Auxilia.Diagnostics;
using Auxilia.Domain.Mailbox;
using Auxilia.Persistence.Tenant;
using Auxilia.SharedKernel.Tenancy;

using Npgsql;

namespace Auxilia.Api.IntegrationTests.Mailbox;

/// <summary>
/// C-04 and C-06 over HTTP (N05, #106): a sync request queues one run per account with the scope and reuses it within
/// the minimum interval; status, calendar events and mail headers are the caller's own; Clients have no access.
/// </summary>
public sealed class MailboxSyncEndpointsTests(ConnectedAccountEndpointsTests.Factory factory) : IClassFixture<ConnectedAccountEndpointsTests.Factory>
{
    private const string Password = "A long Passw0rd for tests!";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sync_QueuesARunPerCalendarAccount_AndReusesItWithinTheInterval()
    {
        var token = await StaffAsync();
        var other = await StaffAsync();
        var account = await ConnectGoogleAsync(token);

        var first = await SyncAsync(token, new StartSyncRequest("calendar", Manual: true));
        var run = first.Runs.ShouldHaveSingleItem();
        (run.AccountId, run.Scope, run.Trigger, run.Status, run.Queued).ShouldBe((account.Id, "calendar", "Manual", "Queued", true));

        var again = (await SyncAsync(token, new StartSyncRequest("calendar", Manual: false))).Runs.Single();
        (again.RunId, again.Queued).ShouldBe((run.RunId, false));

        var status = await GetAsync<SyncRequestResponse>(token, "/api/v1/me/connected-accounts/sync?scope=calendar");
        status.Runs.Single().RunId.ShouldBe(run.RunId);

        // Another user has no account: nothing queued, nothing to see.
        (await SyncAsync(other, new StartSyncRequest("calendar", false))).Runs.ShouldBeEmpty();
        (await GetAsync<SyncRequestResponse>(other, "/api/v1/me/connected-accounts/sync?scope=calendar")).Runs.ShouldBeEmpty();

        // Mail has its own run (C-06).
        var mail = (await SyncAsync(token, new StartSyncRequest("mail", false))).Runs.ShouldHaveSingleItem();
        (mail.AccountId, mail.Scope, mail.Queued).ShouldBe((account.Id, "mail", true));

        using var invalid = await SendAsync(HttpMethod.Post, "/api/v1/me/connected-accounts/sync", token, new StartSyncRequest("contacts", false));
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(invalid)).ShouldBe($"AUX-{EventCodes.Mailbox.SyncScopeInvalid}");
    }

    [Fact]
    public async Task CalendarEvents_AreOnlyTheCallersOwn_InTheTenantTimeZone()
    {
        var token = await StaffAsync();
        var other = await StaffAsync();
        var account = await ConnectGoogleAsync(token);
        var start = new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);
        await StoreEventAsync(account.Id, new CalendarEventData("evt-1", "Riunione con il cliente", "Studio", start, start.AddHours(1), false, false, null));

        var mine = await GetAsync<CalendarEventsResponse>(token, "/api/v1/me/connected-accounts/calendar-events?from=2026-11-01&to=2026-11-30");
        var item = mine.Items.ShouldHaveSingleItem();
        (item.AccountId, item.Title, item.Date, item.Time, item.AllDay).ShouldBe((account.Id, "Riunione con il cliente", new DateOnly(2026, 11, 3), (TimeOnly?)new TimeOnly(10, 0), false));
        mine.TimeZone.ShouldBe("Europe/Rome");

        (await GetAsync<CalendarEventsResponse>(other, "/api/v1/me/connected-accounts/calendar-events?from=2026-11-01&to=2026-11-30")).Items.ShouldBeEmpty();

        using var tooLong = await SendAsync(HttpMethod.Get, "/api/v1/me/connected-accounts/calendar-events?from=2026-01-01&to=2026-12-31", token);
        (await ErrorCodeAsync(tooLong)).ShouldBe($"AUX-{EventCodes.Mailbox.CalendarRangeInvalid}");

        // Disconnecting the account deletes the copied events.
        (await SendAsync(HttpMethod.Delete, $"/api/v1/me/connected-accounts/{account.Id}", token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetAsync<CalendarEventsResponse>(token, "/api/v1/me/connected-accounts/calendar-events?from=2026-11-01&to=2026-11-30")).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task MailHeaders_AreOnlyTheCallersOwn_NewestFirst_WithSearch()
    {
        var token = await StaffAsync();
        var other = await StaffAsync();
        var account = await ConnectGoogleAsync(token);
        var received = new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
        var folderId = await StoreMailAsync(account.Id,
            Message("m1", "Fattura ottobre", received, read: false),
            Message("m2", "Appuntamento", received.AddHours(1), read: true));

        var folders = await GetAsync<List<MailFolderResponse>>(token, "/api/v1/me/connected-accounts/mail-folders");
        var inbox = folders.ShouldHaveSingleItem();
        (inbox.Id, inbox.AccountId, inbox.Kind, inbox.Total, inbox.Unread).ShouldBe((folderId, account.Id, "Inbox", 2, 1));

        var page = await GetAsync<PagedMail>(token, $"/api/v1/me/connected-accounts/mail-messages?folderId={folderId}&pageSize=10");
        page.TotalCount.ShouldBe(2);
        page.Items.Select(item => item.Subject).ShouldBe(["Appuntamento", "Fattura ottobre"]);
        page.Items[1].ShouldSatisfyAllConditions(
            item => (item.FromAddress, item.FromName, item.IsRead).ShouldBe(("mario@example.com", "Mario Rossi", false)),
            item => item.To.ShouldBe(["Studio <studio@example.com>"]),
            item => item.AttachmentNames.ShouldBe(["fattura.pdf"]));

        (await GetAsync<PagedMail>(token, "/api/v1/me/connected-accounts/mail-messages?search=fattura&filter[unread]=true")).Items
            .Select(item => item.Subject).ShouldBe(["Fattura ottobre"]);
        (await GetAsync<PagedMail>(token, "/api/v1/me/connected-accounts/mail-messages?search=studio@example")).TotalCount.ShouldBe(2);
        (await GetAsync<PagedMail>(token, "/api/v1/me/connected-accounts/mail-messages?search=100%25")).TotalCount.ShouldBe(0);

        (await GetAsync<List<MailFolderResponse>>(other, "/api/v1/me/connected-accounts/mail-folders")).ShouldBeEmpty();
        (await GetAsync<PagedMail>(other, $"/api/v1/me/connected-accounts/mail-messages?folderId={folderId}")).TotalCount.ShouldBe(0);

        using var invalid = await SendAsync(HttpMethod.Get, "/api/v1/me/connected-accounts/mail-messages?pageSize=500", token);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(invalid)).ShouldBe($"AUX-{EventCodes.Mailbox.MailListInvalid}");

        // Disconnecting the account deletes folders and headers.
        (await SendAsync(HttpMethod.Delete, $"/api/v1/me/connected-accounts/{account.Id}", token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetAsync<List<MailFolderResponse>>(token, "/api/v1/me/connected-accounts/mail-folders")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Clients_CannotSynchroniseNorReadEvents()
    {
        var (_, userName) = await factory.AddUserAsync([TenantRole.Client], Password);
        var client = await factory.SignInAsync(userName, Password, Ct);

        (await SendAsync(HttpMethod.Post, "/api/v1/me/connected-accounts/sync", client, new StartSyncRequest("calendar", false))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Get, "/api/v1/me/connected-accounts/calendar-events?from=2026-11-01&to=2026-11-30", client)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Get, "/api/v1/me/connected-accounts/mail-folders", client)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Get, "/api/v1/me/connected-accounts/mail-messages", client)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static MailMessageData Message(string id, string subject, DateTimeOffset received, bool read) =>
        new(id, $"<{id}@example.com>", null, "mario@example.com", "Mario Rossi", ["Studio <studio@example.com>"], [], subject, received, received, read, 1024, true, ["fattura.pdf"]);

    private static async Task<Guid> StoreMailAsync(Guid accountId, params MailMessageData[] messages)
    {
        await using var dataSource = NpgsqlDataSource.Create(
            new NpgsqlConnectionStringBuilder(ApiDatabase.Instance.CatalogConnectionString) { Database = "tenant_a" }.ConnectionString);
        await using var db = new TenantDbContext(TenantDbContextOptions.Create(dataSource));
        var folder = MailFolder.Create(Guid.CreateVersion7(), accountId, new MailFolderData("INBOX", "Inbox", MailFolderKind.Inbox), DateTimeOffset.UtcNow);
        db.Add(folder);
        foreach (var message in messages)
        {
            db.Add(MailMessage.Create(Guid.CreateVersion7(), accountId, folder.Id, message, DateTimeOffset.UtcNow));
        }

        await db.SaveChangesAsync(Ct);
        return folder.Id;
    }

    private sealed record PagedMail(List<MailMessageResponse> Items, int Page, int PageSize, long TotalCount);

    private async Task<string> StaffAsync()
    {
        var (_, userName) = await factory.AddUserAsync([TenantRole.Employee], Password);
        return await factory.SignInAsync(userName, Password, Ct);
    }

    private async Task<ConnectedAccountResponse> ConnectGoogleAsync(string token)
    {
        using var start = await SendAsync(HttpMethod.Post, "/api/v1/me/connected-accounts/oauth/google/authorize", token, new StartOAuthRequest(true, true, null));
        var started = (await start.Content.ReadFromJsonAsync<StartOAuthResponse>(Ct))!;
        var state = Uri.UnescapeDataString(started.AuthorizationUrl.Query.Split("state=")[1]);
        using var callback = await SendAsync(HttpMethod.Post, "/api/v1/me/connected-accounts/oauth/google/callback", token, new CompleteOAuthRequest(state, "code", null));
        callback.StatusCode.ShouldBe(HttpStatusCode.OK, await callback.Content.ReadAsStringAsync(Ct));
        return (await callback.Content.ReadFromJsonAsync<ConnectedAccountResponse>(Ct))!;
    }

    private static async Task StoreEventAsync(Guid accountId, CalendarEventData data)
    {
        await using var dataSource = NpgsqlDataSource.Create(
            new NpgsqlConnectionStringBuilder(ApiDatabase.Instance.CatalogConnectionString) { Database = "tenant_a" }.ConnectionString);
        await using var db = new TenantDbContext(TenantDbContextOptions.Create(dataSource));
        db.Add(CalendarEvent.Create(Guid.CreateVersion7(), accountId, data, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(Ct);
    }

    private async Task<SyncRequestResponse> SyncAsync(string token, StartSyncRequest request)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/me/connected-accounts/sync", token, request);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<SyncRequestResponse>(Ct))!;
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString();

    private async Task<T> GetAsync<T>(string token, string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path, token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? body = null)
    {
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request, Ct);
    }
}
