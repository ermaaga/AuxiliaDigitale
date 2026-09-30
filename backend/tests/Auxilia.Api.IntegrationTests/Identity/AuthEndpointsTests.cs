using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Web;

using Auxilia.Api.Endpoints;
using Auxilia.Api.IntegrationTests.Host;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Messaging.Public;
using Auxilia.Contracts.Identity;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Platform;
using Auxilia.Persistence.Catalog;
using Auxilia.Persistence.Tenant;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace Auxilia.Api.IntegrationTests.Identity;

/// <summary>F01/F17 over HTTP: password sign-in, bearer access, refresh rotation and reuse, logout, account links, JWKS.</summary>
public sealed class AuthEndpointsTests : IClassFixture<AuthEndpointsTests.Factory>
{
    public const string ClientId = "test-web";
    private const string Password = "A long enough Passw0rd!";

    private readonly Factory factory;

    public AuthEndpointsTests(Factory factory) => this.factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PasswordGrant_IssuesABearerTokenThatAuthenticatesTheCaller()
    {
        var (userId, userName) = await factory.AddUserAsync([TenantRole.Employee, TenantRole.Administrator], Password);

        var tokens = await SignInAsync(userName, Password);

        tokens.TokenType.ShouldBe("Bearer");
        tokens.ExpiresIn.ShouldBeInRange(9 * 60, 10 * 60);
        using var me = await GetMeAsync(tokens.AccessToken);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await me.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("sub").GetString().ShouldBe(userId.ToString());
        body.GetProperty("tenant").GetString().ShouldBe(ApiDatabase.TenantA);
        body.GetProperty("roles").EnumerateArray().Select(role => role.GetString()).ShouldBe(["Administrator", "Employee"], ignoreOrder: true);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutOrWithABadToken_Is401ProblemDetails()
    {
        using var missing = await GetMeAsync(null);
        using var garbage = await GetMeAsync("not.a.token");

        foreach (var response in new[] { missing, garbage })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            response.Headers.WwwAuthenticate.ToString().ShouldStartWith("Bearer");
            (await ErrorCodeAsync(response)).ShouldBe("AUX-10022");
        }
    }

    [Fact]
    public async Task Token_FromAnotherTenant_IsRejected()
    {
        var (_, userName) = await factory.AddUserAsync([TenantRole.Employee], Password);
        var tokens = await SignInAsync(userName, Password);

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test-auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        request.Headers.Add("X-Tenant", ApiDatabase.TenantB);
        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).ShouldBe("AUX-11004");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(ClientId, null)]
    [InlineData(ClientId, "wrong secret")]
    [InlineData("unknown-client", "whatever")]
    public async Task PasswordGrant_WithABadClient_Is401(string? clientId, string? secret)
    {
        var (_, userName) = await factory.AddUserAsync([TenantRole.Employee], Password);

        using var response = await PostAsync("/api/v1/auth/token", new TokenRequest("password", userName, Password, null), clientId, secret);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ErrorCodeAsync(response)).ShouldBe("AUX-12016");
    }

    [Fact]
    public async Task PasswordGrant_WrongPasswordOrUnknownUser_AreTheSame401()
    {
        var (_, userName) = await factory.AddUserAsync([TenantRole.Employee], Password);

        using var wrong = await PostTokenAsync(new TokenRequest("password", userName, "wrong password!", null));
        using var unknown = await PostTokenAsync(new TokenRequest("password", "nobody-" + Guid.NewGuid().ToString("N")[..6], Password, null));
        using var invalid = await PostTokenAsync(new TokenRequest("client_credentials", null, null, null));

        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ErrorCodeAsync(wrong)).ShouldBe(await ErrorCodeAsync(unknown));
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RefreshGrant_Rotates_AndReuseRevokesTheSession()
    {
        var (_, userName) = await factory.AddUserAsync([TenantRole.Client], Password);
        var first = await SignInAsync(userName, Password);

        using var refreshed = await PostTokenAsync(new TokenRequest("refresh_token", null, null, first.RefreshToken));
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        refreshed.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var second = (await refreshed.Content.ReadFromJsonAsync<TokenResponse>(Ct))!;
        second.RefreshToken.ShouldNotBe(first.RefreshToken);

        using var reused = await PostTokenAsync(new TokenRequest("refresh_token", null, null, first.RefreshToken));
        reused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ErrorCodeAsync(reused)).ShouldBe("AUX-12015");

        // The whole family is revoked: the newer refresh token and the access tokens of the session.
        using var afterReuse = await PostTokenAsync(new TokenRequest("refresh_token", null, null, second.RefreshToken));
        afterReuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var me = await GetMeAsync(second.AccessToken);
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ErrorCodeAsync(me)).ShouldBe("AUX-12023");
    }

    [Fact]
    public async Task Logout_RevokesTheAccessAndRefreshTokens()
    {
        var (_, userName) = await factory.AddUserAsync([TenantRole.Employee], Password);
        var tokens = await SignInAsync(userName, Password);

        using var client = factory.CreateClient();
        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        logout.Headers.Add("X-Tenant", ApiDatabase.TenantA);
        using var response = await client.SendAsync(logout, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var me = await GetMeAsync(tokens.AccessToken);
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ErrorCodeAsync(me)).ShouldBe("AUX-12023");
        using var refresh = await PostTokenAsync(new TokenRequest("refresh_token", null, null, tokens.RefreshToken));
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ForgotAndResetPassword_ChangeThePasswordAndEndTheSessions()
    {
        var (_, userName) = await factory.AddUserAsync([TenantRole.Client], Password);
        var before = await SignInAsync(userName, Password);

        using var unknown = await PostAsync("/api/v1/auth/password/forgot", new ForgotPasswordRequest("nobody-" + Guid.NewGuid().ToString("N")[..6]));
        using var forgot = await PostAsync("/api/v1/auth/password/forgot", new ForgotPasswordRequest(userName));
        unknown.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        forgot.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var message = factory.Messages.Single(item => item.Model["name"] as string == userName);
        message.TemplateCode.ShouldBe(MessageTemplates.PasswordReset);
        var link = new Uri((string)message.Model["link"]!);
        link.AbsolutePath.ShouldBe($"/{ApiDatabase.TenantA}/reset-password");
        var token = HttpUtility.ParseQueryString(link.Query)["token"]!;

        using var weak = await PostAsync("/api/v1/auth/password/reset", new ResetPasswordRequest(token, "short"));
        weak.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var reset = await PostAsync("/api/v1/auth/password/reset", new ResetPasswordRequest(token, "A brand new long Passw0rd!"));
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var reused = await PostAsync("/api/v1/auth/password/reset", new ResetPasswordRequest(token, "Another new long Passw0rd!"));
        reused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(reused)).ShouldBe("AUX-12018");

        (await SignInAsync(userName, "A brand new long Passw0rd!")).AccessToken.ShouldNotBeNullOrEmpty();
        using var old = await GetMeAsync(before.AccessToken);
        old.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Activate_SetsTheFirstPassword()
    {
        var (userId, userName) = await factory.AddUserAsync([TenantRole.Client], password: null);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<Application.Abstractions.Tenancy.ITenantContextSetter>()
                .Set((await scope.ServiceProvider.GetRequiredService<Application.Abstractions.Tenancy.ITenantDirectory>().FindBySlugAsync(ApiDatabase.TenantA, Ct))!);
            (await scope.ServiceProvider.GetRequiredService<Application.Identity.IAccountLinkManager>().SendActivationAsync(userId, Ct)).IsSuccess.ShouldBeTrue();
        }

        var message = factory.Messages.Single(item => item.Model["name"] as string == userName);
        message.TemplateCode.ShouldBe(MessageTemplates.AccountActivation);
        var token = HttpUtility.ParseQueryString(new Uri((string)message.Model["link"]!).Query)["token"]!;

        using var activate = await PostAsync("/api/v1/auth/activate", new ActivateAccountRequest(token, Password));

        activate.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SignInAsync(userName, Password)).AccessToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Jwks_PublishesTheValidationKeys()
    {
        using var client = factory.CreateClient();

        var jwks = await client.GetFromJsonAsync<JsonElement>("/.well-known/jwks.json", Ct);

        var key = jwks.GetProperty("keys").EnumerateArray().ShouldHaveSingleItem();
        (key.GetProperty("kty").GetString(), key.GetProperty("alg").GetString()).ShouldBe(("EC", "ES256"));
        key.TryGetProperty("d", out _).ShouldBeFalse();
    }

    private async Task<TokenResponse> SignInAsync(string userName, string password)
    {
        using var response = await PostTokenAsync(new TokenRequest("password", userName, password, null));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(Ct))!;
    }

    private Task<HttpResponseMessage> PostTokenAsync(TokenRequest body) =>
        PostAsync("/api/v1/auth/token", body, ClientId, factory.ClientSecret);

    private async Task<HttpResponseMessage> PostAsync<T>(string path, T body, string? clientId = null, string? secret = null)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Tenant", ApiDatabase.TenantA);
        if (clientId is not null)
        {
            request.Headers.Add("X-Client-Id", clientId);
        }

        if (secret is not null)
        {
            request.Headers.Add("X-Client-Secret", secret);
        }

        return await client.SendAsync(request, Ct);
    }

    private async Task<HttpResponseMessage> GetMeAsync(string? accessToken)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test-auth/me");
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await client.SendAsync(request, Ct);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString();

    public class Factory : ApiFactory, IAsyncLifetime
    {
        private static readonly SemaphoreSlim ClientLock = new(1, 1);

        private readonly RecordingDispatcher dispatcher = new();

        /// <summary>The same for every factory: parallel test classes (re)register the same client.</summary>
        public string ClientSecret { get; } = "integration-tests-client-credential";

        public IReadOnlyCollection<OutboundMessageRequest> Messages => dispatcher.Requests.ToArray();

        protected override IApiEndpoints? Endpoints { get; } = new MeEndpoints();

        public virtual async ValueTask InitializeAsync()
        {
            // Test classes run in parallel and share the client row: register it one factory at a time.
            await ClientLock.WaitAsync();
            try
            {
                var hasher = Services.GetRequiredService<IPasswordHasher>();
                var options = new DbContextOptionsBuilder<CatalogDbContext>();
                CatalogPersistence.Configure(options, ApiDatabase.Instance.CatalogConnectionString);
                await using var catalog = new CatalogDbContext(options.Options);
                var client = await catalog.ClientApplications.SingleOrDefaultAsync(item => item.ClientId == ClientId);
                if (client is null)
                {
                    client = ClientApplication.Create(Guid.CreateVersion7(), ClientId, "Test web", ClientApplicationType.WebBff).Value;
                    catalog.ClientApplications.Add(client);
                }

                client.SetSecretHash(hasher.Hash(ClientSecret));
                await catalog.SaveChangesAsync();
            }
            finally
            {
                ClientLock.Release();
            }
        }

        /// <summary>A user of tenant A with a unique user name (and a password unless null).</summary>
        public async Task<(Guid UserId, string UserName)> AddUserAsync(TenantRole[] roles, string? password)
        {
            var connectionString = new NpgsqlConnectionStringBuilder(ApiDatabase.Instance.CatalogConnectionString) { Database = "tenant_a" }.ConnectionString;
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await using var db = new TenantDbContext(TenantDbContextOptions.Create(dataSource));
            var person = new Person(Guid.CreateVersion7(), "Mario", "Rossi", null);
            db.Set<Person>().Add(person);
            var userName = "user-" + Guid.NewGuid().ToString("N")[..10];
            var user = User.Create(Guid.CreateVersion7(), person.Id, userName, userName + "@example.test", "it", roles, isActive: true).Value;
            if (password is not null)
            {
                user.SetPassword(Services.GetRequiredService<IPasswordHasher>().Hash(password), PasswordFormat.Identity, DateTimeOffset.UtcNow);
            }

            db.Set<User>().Add(user);
            await db.SaveChangesAsync();
            return (user.Id, userName);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.AddSingleton<IMessageDispatcher>(dispatcher));
        }

        /// <summary>Signs in with the password grant and returns the access token.</summary>
        public async Task<string> SignInAsync(string userName, string password, CancellationToken cancellationToken)
        {
            using var client = CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/token")
            {
                Content = JsonContent.Create(new TokenRequest("password", userName, password, null)),
            };
            request.Headers.Add("X-Tenant", ApiDatabase.TenantA);
            request.Headers.Add("X-Client-Id", ClientId);
            request.Headers.Add("X-Client-Secret", ClientSecret);
            using var response = await client.SendAsync(request, cancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
            return (await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken))!.AccessToken;
        }
    }

    private sealed class MeEndpoints : IApiEndpoints
    {
        public void Map(RouteGroupBuilder api) =>
            api.MapGet("/test-auth/me", (ClaimsPrincipal user) => TypedResults.Ok(new
            {
                sub = user.FindFirstValue("sub"),
                tenant = user.FindFirstValue("tenant"),
                roles = user.FindAll("role").Select(claim => claim.Value).ToArray(),
            })).RequireAuthorization().ExcludeFromDescription();
    }

    private sealed class RecordingDispatcher : IMessageDispatcher
    {
        private readonly ConcurrentQueue<OutboundMessageRequest> requests = new();

        public IEnumerable<OutboundMessageRequest> Requests => requests;

        public Task<Result<Guid>> QueueAsync(OutboundMessageRequest request, CancellationToken cancellationToken)
        {
            requests.Enqueue(request);
            return Task.FromResult(Result.Success(Guid.CreateVersion7()));
        }
    }
}
