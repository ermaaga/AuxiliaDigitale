using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Identity;
using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Contracts.Mailbox;
using Auxilia.Diagnostics;
using Auxilia.Domain.Mailbox;
using Auxilia.Persistence.Tenant;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Npgsql;

namespace Auxilia.Api.IntegrationTests.Mailbox;

/// <summary>
/// C-02 over HTTP (N05, ADR 0021): "Sign in with Google" start → callback with a fake provider, PEC / IMAP with the SSRF
/// check, colour, disconnect; secrets stored protected; accounts visible only to their owner; Clients have no access.
/// </summary>
public sealed class ConnectedAccountEndpointsTests(ConnectedAccountEndpointsTests.Factory factory) : IClassFixture<ConnectedAccountEndpointsTests.Factory>
{
    private const string Password = "A long Passw0rd for tests!";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GoogleAccount_IsConnectedThroughStartAndCallback_ThenDisconnected()
    {
        var (_, token) = await StaffAsync();

        (await GetAsync<ConnectedAccountProviderResponse[]>(token, "/api/v1/me/connected-accounts/providers"))
            .Select(provider => provider.Provider).ShouldBe(["google", "imap"]);

        var started = await PostAsync<StartOAuthResponse>(token, "/api/v1/me/connected-accounts/oauth/google/authorize", new StartOAuthRequest(true, true, null));
        var state = Uri.UnescapeDataString(started.AuthorizationUrl.Query.Split("state=")[1]);

        // Another user cannot complete my consent.
        var (_, other) = await StaffAsync();
        using (var stolen = await SendAsync(HttpMethod.Post, "/api/v1/me/connected-accounts/oauth/google/callback", other, new CompleteOAuthRequest(state, "code", null)))
        {
            (await ErrorCodeAsync(stolen)).ShouldBe(Code(EventCodes.Mailbox.OAuthStateInvalid));
        }

        var connected = await PostAsync<ConnectedAccountResponse>(token, "/api/v1/me/connected-accounts/oauth/google/callback", new CompleteOAuthRequest(state, "code", null));
        (connected.Provider, connected.HasMail, connected.HasCalendar, connected.Status).ShouldBe(("google", true, true, "Active"));
        connected.Address.ShouldEndWith("@gmail.com");

        using (var replayed = await SendAsync(HttpMethod.Post, "/api/v1/me/connected-accounts/oauth/google/callback", token, new CompleteOAuthRequest(state, "code", null)))
        {
            (await ErrorCodeAsync(replayed)).ShouldBe(Code(EventCodes.Mailbox.OAuthStateInvalid));
        }

        var stored = await StoredAsync(connected.Id);
        stored.ProtectedSecret.ShouldNotContain("refresh-");
        stored.ProtectedSecret.Length.ShouldBeGreaterThan(40);
        (await RawAsync(token, "/api/v1/me/connected-accounts")).ShouldNotContain("protected", Case.Insensitive);

        (await SendAsync(HttpMethod.Delete, $"/api/v1/me/connected-accounts/{connected.Id}", other)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/me/connected-accounts/{connected.Id}", token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetAsync<ConnectedAccountResponse[]>(token, "/api/v1/me/connected-accounts")).ShouldBeEmpty();
        factory.Google.Revoked.ShouldContain(factory.Google.RefreshFor(connected.Address));
    }

    [Fact]
    public async Task ImapAccount_IsTestedAndConnected_AndOnlyItsOwnerSeesAndChangesIt()
    {
        var (_, token) = await StaffAsync();
        var (_, other) = await StaffAsync();

        (await GetAsync<ImapServerSuggestionResponse>(token, "/api/v1/me/connected-accounts/imap-server?address=studio@pec.it"))
            .ShouldBe(new ImapServerSuggestionResponse("imaps.pec.aruba.it", 993, "SslOnConnect"));

        var account = await PostAsync<ConnectedAccountResponse>(token, "/api/v1/me/connected-accounts/imap",
            new ConnectImapRequest("studio@pec.it", "studio@pec.it", "pec-password", "imaps.pec.aruba.it", 993, "SslOnConnect"));
        (account.Provider, account.HasMail, account.HasCalendar, account.ImapHost, account.Color).ShouldBe(("imap", true, false, "imaps.pec.aruba.it", "blue"));
        (await StoredAsync(account.Id)).ProtectedSecret.ShouldNotContain("pec-password");

        (await GetAsync<ConnectedAccountResponse[]>(other, "/api/v1/me/connected-accounts")).ShouldBeEmpty();
        (await SendAsync(HttpMethod.Put, $"/api/v1/me/connected-accounts/{account.Id}", other, new UpdateConnectedAccountRequest("red"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PutAsync<ConnectedAccountResponse>(token, $"/api/v1/me/connected-accounts/{account.Id}", new UpdateConnectedAccountRequest("red"))).Color.ShouldBe("red");
    }

    [Fact]
    public async Task ImapServers_OnPrivateAddressesOrWrongCredentials_AreRefused()
    {
        var (_, token) = await StaffAsync();

        using (var internalHost = await SendAsync(HttpMethod.Post, "/api/v1/me/connected-accounts/imap", token,
                   new ConnectImapRequest("me@corp.it", "me", "pw", "intranet.corp.it", 993, "SslOnConnect")))
        {
            internalHost.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ErrorCodeAsync(internalHost)).ShouldBe(Code(EventCodes.Mailbox.ImapHostRefused));
        }

        using (var literal = await SendAsync(HttpMethod.Post, "/api/v1/me/connected-accounts/imap", token,
                   new ConnectImapRequest("me@corp.it", "me", "pw", "127.0.0.1", 25, "Plain")))
        {
            (await ErrorCodeAsync(literal)).ShouldBe(Code(EventCodes.Mailbox.AccountInvalid));
        }

        using var wrong = await SendAsync(HttpMethod.Post, "/api/v1/me/connected-accounts/imap", token,
            new ConnectImapRequest("studio@pec.it", "studio@pec.it", FakeImap.WrongPassword, "imaps.pec.aruba.it", 993, "SslOnConnect"));
        (await ErrorCodeAsync(wrong)).ShouldBe(Code(EventCodes.Mailbox.ImapAuthenticationFailed));
        (await GetAsync<ConnectedAccountResponse[]>(token, "/api/v1/me/connected-accounts")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Clients_HaveNoConnectedAccounts()
    {
        var (_, userName) = await factory.AddUserAsync([TenantRole.Client], Password);
        var client = await factory.SignInAsync(userName, Password, Ct);

        (await SendAsync(HttpMethod.Get, "/api/v1/me/connected-accounts", client)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Post, "/api/v1/me/connected-accounts/oauth/google/authorize", client, new StartOAuthRequest(true, false, null)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static string Code(int code) => $"AUX-{code}";

    private async Task<(Guid UserId, string Token)> StaffAsync()
    {
        var (userId, userName) = await factory.AddUserAsync([TenantRole.Employee], Password);
        return (userId, await factory.SignInAsync(userName, Password, Ct));
    }

    private static async Task<ConnectedAccount> StoredAsync(Guid id)
    {
        await using var dataSource = NpgsqlDataSource.Create(
            new NpgsqlConnectionStringBuilder(ApiDatabase.Instance.CatalogConnectionString) { Database = "tenant_a" }.ConnectionString);
        await using var db = new TenantDbContext(TenantDbContextOptions.Create(dataSource));
        return await db.Set<ConnectedAccount>().AsNoTracking().SingleAsync(account => account.Id == id, Ct);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString();

    private async Task<T> GetAsync<T>(string token, string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path, token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private async Task<string> RawAsync(string token, string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path, token);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task<T> PostAsync<T>(string token, string path, object body)
    {
        using var response = await SendAsync(HttpMethod.Post, path, token, body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private async Task<T> PutAsync<T>(string token, string path, object body)
    {
        using var response = await SendAsync(HttpMethod.Put, path, token, body);
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

    /// <summary>The API with a fake Google (no network), a fake IMAP server and a DNS answer per host.</summary>
    public sealed class Factory : AuthEndpointsTests.Factory
    {
        public FakeGoogle Google { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOAuthProvider>();
                services.AddSingleton<IOAuthProvider>(Google);
                services.RemoveAll<IImapConnectionTester>();
                services.AddSingleton<IImapConnectionTester, FakeImap>();
                services.RemoveAll<IHostAddressResolver>();
                services.AddSingleton<IHostAddressResolver, FakeDns>();
            });
        }
    }

    /// <summary>Google that accepts any code: every exchange is a new account with its own refresh token.</summary>
    public sealed class FakeGoogle : IOAuthProvider
    {
        private readonly Dictionary<string, string> refreshByAddress = [];

        public List<string> Revoked { get; } = [];

        public ConnectedAccountProvider Provider => ConnectedAccountProvider.Google;

        public bool IsConfigured => true;

        public bool SupportsCalendar => true;

        public string RefreshFor(string address)
        {
            lock (refreshByAddress)
            {
                return refreshByAddress[address];
            }
        }

        public Uri AuthorizationUrl(OAuthAuthorization authorization) =>
            new($"https://accounts.example/consent?challenge={authorization.CodeChallenge}&state={authorization.State}");

        public Task<Result<OAuthTokens>> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken)
        {
            var id = Guid.NewGuid().ToString("N");
            var address = $"{id}@gmail.com";
            lock (refreshByAddress)
            {
                refreshByAddress[address] = $"refresh-{id}";
            }

            return Task.FromResult(Result.Success(new OAuthTokens(
                "access", $"refresh-{id}", DateTimeOffset.UtcNow.AddHours(1), "openid gmail calendar", true, true, address, "Test User")));
        }

        public Task<Result<OAuthTokens>> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OAuthRevocation> RevokeAsync(string refreshToken, CancellationToken cancellationToken)
        {
            lock (Revoked)
            {
                Revoked.Add(refreshToken);
            }

            return Task.FromResult(OAuthRevocation.Revoked);
        }
    }

    private sealed class FakeImap : IImapConnectionTester
    {
        public const string WrongPassword = "wrong-password";

        public Task<ImapTestOutcome> TestAsync(ImapConnectionTarget target, string userName, string password, CancellationToken cancellationToken) =>
            Task.FromResult(password == WrongPassword ? ImapTestOutcome.AuthenticationFailed : ImapTestOutcome.Success);
    }

    private sealed class FakeDns : IHostAddressResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IPAddress>>(host == "intranet.corp.it" ? [IPAddress.Parse("10.0.0.12")] : [IPAddress.Parse("62.149.157.1")]);
    }
}
