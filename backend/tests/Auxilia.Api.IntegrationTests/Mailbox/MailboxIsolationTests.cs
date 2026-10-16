using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Identity;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Contracts.Identity;
using Auxilia.Contracts.Mailbox;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Mailbox;
using Auxilia.Persistence.Tenant;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace Auxilia.Api.IntegrationTests.Mailbox;

/// <summary>
/// C-08 (N05): a connected account and everything copied from it belong to its owner only. An Administrator of the same
/// tenant and a user of another tenant reach none of it through any mailbox endpoint (lists empty, ids 404, nothing changed),
/// and the owner's token does not open another tenant. Platform tokens are refused on every endpoint by
/// <c>SecurityPerimeterTests</c>. Deleting the employee removes their accounts with every copy.
/// </summary>
public sealed class MailboxIsolationTests(ConnectedAccountEndpointsTests.Factory factory) : IClassFixture<ConnectedAccountEndpointsTests.Factory>
{
    private const string Password = "A long Passw0rd for tests!";
    private const string Base = "/api/v1/me/connected-accounts";
    private const string Range = "from=2026-11-01&to=2026-11-30";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnotherUserOrTenant_ReachesNothingOfTheOwnersAccount()
    {
        var (_, owner) = await SignInAsync(ApiDatabase.TenantA, "tenant_a", TenantRole.Employee);
        var (_, administrator) = await SignInAsync(ApiDatabase.TenantA, "tenant_a", TenantRole.Administrator);
        var (_, otherTenant) = await SignInAsync(ApiDatabase.TenantB, "tenant_b", TenantRole.Administrator);
        var account = await ConnectGoogleAsync(owner);
        var (folderId, messageId) = await StoreMailAsync(account.Id);
        await StoreEventAsync(account.Id);
        var run = (await ReadAsync<SyncRequestResponse>(HttpMethod.Post, $"{Base}/sync", owner, body: new StartSyncRequest("mail", false))).Runs.ShouldHaveSingleItem();

        foreach (var (stranger, tenant) in new[] { (administrator, ApiDatabase.TenantA), (otherTenant, ApiDatabase.TenantB) })
        {
            (await ReadAsync<List<ConnectedAccountResponse>>(HttpMethod.Get, Base, stranger, tenant)).ShouldBeEmpty();
            (await ReadAsync<List<MailFolderResponse>>(HttpMethod.Get, $"{Base}/mail-folders", stranger, tenant)).ShouldBeEmpty();
            (await ReadAsync<JsonElement>(HttpMethod.Get, $"{Base}/mail-messages?folderId={folderId}", stranger, tenant)).GetProperty("totalCount").GetInt64().ShouldBe(0);
            (await ReadAsync<JsonElement>(HttpMethod.Get, $"{Base}/mail-messages?search=fattura", stranger, tenant)).GetProperty("totalCount").GetInt64().ShouldBe(0);
            (await ReadAsync<CalendarEventsResponse>(HttpMethod.Get, $"{Base}/calendar-events?{Range}", stranger, tenant)).Items.ShouldBeEmpty();
            (await ReadAsync<SyncRequestResponse>(HttpMethod.Get, $"{Base}/sync?scope=mail", stranger, tenant)).Runs.ShouldBeEmpty();

            // A sync request of the stranger never queues a run for the owner's account.
            (await ReadAsync<SyncRequestResponse>(HttpMethod.Post, $"{Base}/sync", stranger, tenant, new StartSyncRequest("mail", true))).Runs.ShouldBeEmpty();

            foreach (var (method, path, body) in new (HttpMethod, string, object?)[]
            {
                (HttpMethod.Get, $"{Base}/mail-messages/{messageId}", null),
                (HttpMethod.Get, $"{Base}/mail-messages/{messageId}/eml", null),
                (HttpMethod.Get, $"{Base}/mail-messages/{messageId}/attachments/0", null),
                (HttpMethod.Post, $"{Base}/mail-messages/{messageId}/file", new FileMailRequest(Guid.CreateVersion7(), null, null, null, true)),
                (HttpMethod.Put, $"{Base}/{account.Id}", new UpdateConnectedAccountRequest("rose")),
                (HttpMethod.Delete, $"{Base}/{account.Id}", null),
            })
            {
                using var response = await SendAsync(method, path, stranger, tenant, body);
                response.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"{method} {path} as a user of {tenant}");
            }
        }

        // The owner's token does not switch tenant.
        using (var switched = await SendAsync(HttpMethod.Get, Base, owner, ApiDatabase.TenantB))
        {
            switched.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await switched.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString().ShouldBe("AUX-11004");
        }

        // Nothing of the owner changed.
        var mine = (await ReadAsync<List<ConnectedAccountResponse>>(HttpMethod.Get, Base, owner)).ShouldHaveSingleItem();
        (mine.Id, mine.Color).ShouldBe((account.Id, account.Color));
        (await ReadAsync<List<MailFolderResponse>>(HttpMethod.Get, $"{Base}/mail-folders", owner)).ShouldHaveSingleItem().Total.ShouldBe(1);
        (await ReadAsync<CalendarEventsResponse>(HttpMethod.Get, $"{Base}/calendar-events?{Range}", owner)).Items.ShouldHaveSingleItem();
        (await ReadAsync<SyncRequestResponse>(HttpMethod.Get, $"{Base}/sync?scope=mail", owner)).Runs.ShouldHaveSingleItem().RunId.ShouldBe(run.RunId);
    }

    [Fact]
    public async Task DeletingTheEmployee_RemovesTheirConnectedAccountsAndCopies()
    {
        var (employeeId, employee) = await SignInAsync(ApiDatabase.TenantA, "tenant_a", TenantRole.Employee);
        var (_, administrator) = await SignInAsync(ApiDatabase.TenantA, "tenant_a", TenantRole.Administrator);
        var account = await ConnectGoogleAsync(employee);
        await StoreMailAsync(account.Id);
        await StoreEventAsync(account.Id);

        using var deleted = await SendAsync(HttpMethod.Delete, $"/api/v1/employees/{employeeId}", administrator);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync(Ct));

        await using var dataSource = TenantA();
        await using var db = new TenantDbContext(TenantDbContextOptions.Create(dataSource));
        db.Set<ConnectedAccount>().Count(item => item.UserId == employeeId).ShouldBe(0);
        db.Set<MailMessage>().Count(item => item.AccountId == account.Id).ShouldBe(0);
        db.Set<CalendarEvent>().Count(item => item.AccountId == account.Id).ShouldBe(0);
    }

    /// <summary>A staff user of the given tenant (database created by <see cref="ApiDatabase"/>) signed in with the password grant.</summary>
    private async Task<(Guid UserId, string Token)> SignInAsync(string tenant, string database, TenantRole role)
    {
        await using var dataSource = NpgsqlDataSource.Create(
            new NpgsqlConnectionStringBuilder(ApiDatabase.Instance.CatalogConnectionString) { Database = database }.ConnectionString);
        await using var db = new TenantDbContext(TenantDbContextOptions.Create(dataSource));
        var person = new Person(Guid.CreateVersion7(), "Giulia", "Bassi", null);
        db.Set<Person>().Add(person);
        var userName = "mailbox-" + Guid.NewGuid().ToString("N")[..10];
        var user = User.Create(Guid.CreateVersion7(), person.Id, userName, userName + "@example.test", "it", [role], isActive: true).Value;
        user.SetPassword(factory.Services.GetRequiredService<IPasswordHasher>().Hash(Password), PasswordFormat.Identity, DateTimeOffset.UtcNow);
        db.Set<User>().Add(user);
        await db.SaveChangesAsync(Ct);

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/token")
        {
            Content = JsonContent.Create(new TokenRequest("password", userName, Password, null)),
        };
        request.Headers.Add("X-Tenant", tenant);
        request.Headers.Add("X-Client-Id", AuthEndpointsTests.ClientId);
        request.Headers.Add("X-Client-Secret", factory.ClientSecret);
        using var response = await client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (user.Id, (await response.Content.ReadFromJsonAsync<TokenResponse>(Ct))!.AccessToken);
    }

    private async Task<ConnectedAccountResponse> ConnectGoogleAsync(string token)
    {
        var started = await ReadAsync<StartOAuthResponse>(HttpMethod.Post, $"{Base}/oauth/google/authorize", token, body: new StartOAuthRequest(true, true, null));
        var state = Uri.UnescapeDataString(started.AuthorizationUrl.Query.Split("state=")[1]);
        return await ReadAsync<ConnectedAccountResponse>(HttpMethod.Post, $"{Base}/oauth/google/callback", token, body: new CompleteOAuthRequest(state, "code", null));
    }

    private static async Task<(Guid FolderId, Guid MessageId)> StoreMailAsync(Guid accountId)
    {
        await using var dataSource = TenantA();
        await using var db = new TenantDbContext(TenantDbContextOptions.Create(dataSource));
        var folder = MailFolder.Create(Guid.CreateVersion7(), accountId, new MailFolderData("INBOX", "Inbox", MailFolderKind.Inbox), DateTimeOffset.UtcNow);
        var received = new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
        var message = MailMessage.Create(Guid.CreateVersion7(), accountId, folder.Id,
            new MailMessageData("iso-1", "<iso-1@example.com>", null, "mario@example.com", "Mario Rossi", ["studio@example.com"], [],
                "Fattura ottobre", received, received, false, 512, true, ["fattura.pdf"]),
            DateTimeOffset.UtcNow);
        db.AddRange(folder, message);
        await db.SaveChangesAsync(Ct);
        return (folder.Id, message.Id);
    }

    private static async Task StoreEventAsync(Guid accountId)
    {
        await using var dataSource = TenantA();
        await using var db = new TenantDbContext(TenantDbContextOptions.Create(dataSource));
        var start = new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);
        db.Add(CalendarEvent.Create(Guid.CreateVersion7(), accountId,
            new CalendarEventData("iso-evt", "Riunione", "Studio", start, start.AddHours(1), false, false, null), DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(Ct);
    }

    private static NpgsqlDataSource TenantA() =>
        NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(ApiDatabase.Instance.CatalogConnectionString) { Database = "tenant_a" }.ConnectionString);

    private async Task<T> ReadAsync<T>(HttpMethod method, string path, string token, string? tenant = null, object? body = null)
    {
        using var response = await SendAsync(method, path, token, tenant, body);
        response.IsSuccessStatusCode.ShouldBeTrue($"{method} {path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, string? tenant = null, object? body = null)
    {
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (tenant is not null)
        {
            request.Headers.Add("X-Tenant", tenant);
        }

        return await http.SendAsync(request, Ct);
    }
}
