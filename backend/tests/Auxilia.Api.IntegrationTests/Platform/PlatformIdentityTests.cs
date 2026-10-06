using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using Auxilia.Api.Authorization;
using Auxilia.Api.Endpoints;
using Auxilia.Api.IntegrationTests.Host;
using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Cases;
using Auxilia.Application.Identity;
using Auxilia.Contracts.Identity;
using Auxilia.Contracts.Platform;
using Auxilia.Domain.Platform;
using Auxilia.Persistence.Catalog;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Api.IntegrationTests.Platform;

/// <summary>
/// N02 over HTTP: platform user activation with TOTP (D-22), console sign-in, tenant-scoped platform token, and D-21:
/// platform tokens never reach business endpoints.
/// </summary>
public sealed partial class PlatformIdentityTests : IClassFixture<PlatformIdentityTests.Factory>
{
    private const string ConsoleClient = "test-console";
    private const string ConsoleSecret = "integration-tests-console-credential";
    private const string Password = "a long enough platform password";

    private readonly Factory factory;

    public PlatformIdentityTests(Factory factory) => this.factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ActivationAndSignIn_NeedPasswordAndAuthenticatorCode()
    {
        var (email, secret) = await ActivatedUserAsync();

        using var wrongCode = await PostTokenAsync(new PlatformTokenRequest("password", email, Password, "000000", null));
        using var wrongPassword = await PostTokenAsync(new PlatformTokenRequest("password", email, "wrong password!!", Code(secret, 1), null));
        wrongCode.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ErrorCodeAsync(wrongCode)).ShouldBe(await ErrorCodeAsync(wrongPassword));

        var tokens = await SignInAsync(email, secret);
        using var me = await SendAsync(HttpMethod.Get, "/api/v1/platform/me", tokens.AccessToken);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = (await me.Content.ReadFromJsonAsync<PlatformMeResponse>(Ct))!;
        (body.Email, body.Roles.Single()).ShouldBe((email, "System"));

        using var refreshed = await PostTokenAsync(new PlatformTokenRequest("refresh_token", null, null, null, tokens.RefreshToken));
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ConsoleClient_IsForTheConsoleOnly_AndTenantClientsCannotSignInToTheConsole()
    {
        using var client = factory.CreateClient();
        using var tenantSignIn = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/token")
        {
            Content = JsonContent.Create(new TokenRequest("password", "nobody", "whatever password", null)),
        };
        tenantSignIn.Headers.Add("X-Tenant", ApiDatabase.TenantA);
        tenantSignIn.Headers.Add("X-Client-Id", ConsoleClient);
        tenantSignIn.Headers.Add("X-Client-Secret", ConsoleSecret);
        using var response = await client.SendAsync(tenantSignIn, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ErrorCodeAsync(response)).ShouldBe("AUX-12016");
    }

    [Fact]
    public async Task TenantToken_OpensTechnicalEndpointsOnly_NeverBusinessOnes()
    {
        var (email, secret) = await ActivatedUserAsync();
        var console = (await SignInAsync(email, secret)).AccessToken;

        using var list = await SendAsync(HttpMethod.Get, "/api/v1/platform/tenants", console);
        var tenants = (await list.Content.ReadFromJsonAsync<PlatformTenantResponse[]>(Ct))!;
        tenants.Select(tenant => tenant.Slug).ShouldContain(ApiDatabase.TenantA);
        tenants.Select(tenant => tenant.Slug).ShouldNotContain(ApiDatabase.Archived);

        using var opened = await SendAsync(HttpMethod.Post, $"/api/v1/platform/tenants/{ApiDatabase.TenantA}/token", console);
        opened.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tenantToken = (await opened.Content.ReadFromJsonAsync<PlatformTenantTokenResponse>(Ct))!.AccessToken;

        // Technical endpoint of the tenant: the caller is the platform user.
        using var technical = await SendAsync(HttpMethod.Get, "/api/v1/test-platform/whoami", tenantToken);
        technical.StatusCode.ShouldBe(HttpStatusCode.OK);
        var whoami = await technical.Content.ReadFromJsonAsync<JsonElement>(Ct);
        (whoami.GetProperty("actor").GetString(), whoami.GetProperty("tenant").GetString()).ShouldBe(("Platform", ApiDatabase.TenantA));

        // D-21: business endpoints do not exist for platform tokens, endpoints of the tenant user refuse them.
        (await SendAsync(HttpMethod.Get, "/api/v1/test-business/services", tenantToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        foreach (var path in new[] { "/api/v1/me", "/api/v1/me/navigation" })
        {
            using var mine = await SendAsync(HttpMethod.Get, path, tenantToken);
            mine.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await mine.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString().ShouldBe("AUX-12071");
        }

        // Each token only where it belongs.
        await ShouldBeForbiddenAsync(await SendAsync(HttpMethod.Get, "/api/v1/platform/me", tenantToken));
        await ShouldBeForbiddenAsync(await SendAsync(HttpMethod.Get, "/api/v1/test-platform/whoami", console));
        (await SendAsync(HttpMethod.Post, "/api/v1/platform/tenants/no-such-tenant/token", console)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Post, $"/api/v1/platform/tenants/{ApiDatabase.Archived}/token", console)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SuspendedTenant_StaysReachableForItsTechnicalEndpoints()
    {
        var (email, secret) = await ActivatedUserAsync();
        var console = (await SignInAsync(email, secret)).AccessToken;
        using var opened = await SendAsync(HttpMethod.Post, $"/api/v1/platform/tenants/{ApiDatabase.Suspended}/token", console);
        var tenantToken = (await opened.Content.ReadFromJsonAsync<PlatformTenantTokenResponse>(Ct))!.AccessToken;

        (await SendAsync(HttpMethod.Get, "/api/v1/test-platform/whoami", tenantToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TenantUserTokens_AreRejectedByPlatformEndpoints()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/me");
        request.Headers.Add(ApiFactory.TestTenantClaimHeader, ApiDatabase.TenantA);
        request.Headers.Add(ApiFactory.TestRolesHeader, "Administrator");

        await ShouldBeForbiddenAsync(await client.SendAsync(request, Ct));
    }

    [Fact]
    public async Task Logout_RevokesTheConsoleAndTheTenantTokensOfTheSession()
    {
        var (email, secret) = await ActivatedUserAsync();
        var tokens = await SignInAsync(email, secret);
        using var opened = await SendAsync(HttpMethod.Post, $"/api/v1/platform/tenants/{ApiDatabase.TenantA}/token", tokens.AccessToken);
        var tenantToken = (await opened.Content.ReadFromJsonAsync<PlatformTenantTokenResponse>(Ct))!.AccessToken;

        (await SendAsync(HttpMethod.Post, "/api/v1/platform/auth/logout", tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var console = await SendAsync(HttpMethod.Get, "/api/v1/platform/me", tokens.AccessToken);
        using var tenant = await SendAsync(HttpMethod.Get, "/api/v1/test-platform/whoami", tenantToken);
        console.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        tenant.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ErrorCodeAsync(tenant)).ShouldBe("AUX-12023");
        (await PostTokenAsync(new PlatformTokenRequest("refresh_token", null, null, null, tokens.RefreshToken))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Activation_RejectsAWrongCodeAndAUsedToken()
    {
        var token = await factory.AddPlatformUserAsync("wrong-code-" + Guid.NewGuid().ToString("N")[..6] + "@example.test");
        using var enrollment = await PostAsync("/api/v1/platform/auth/enrollment", new PlatformEnrollmentRequest(token));
        var secret = (await enrollment.Content.ReadFromJsonAsync<PlatformEnrollmentResponse>(Ct))!.Secret;

        using var wrong = await PostAsync("/api/v1/platform/auth/activate", new PlatformActivateRequest(token, Password, "000000"));
        using var ok = await PostAsync("/api/v1/platform/auth/activate", new PlatformActivateRequest(token, Password, Code(secret, 0)));
        using var again = await PostAsync("/api/v1/platform/auth/activate", new PlatformActivateRequest(token, Password, Code(secret, 1)));

        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(wrong)).ShouldBe("AUX-12041");
        ok.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(again)).ShouldBe("AUX-12018");
    }

    private async Task<(string Email, string Secret)> ActivatedUserAsync()
    {
        var email = "ops-" + Guid.NewGuid().ToString("N")[..8] + "@example.test";
        var token = await factory.AddPlatformUserAsync(email);

        using var enrollment = await PostAsync("/api/v1/platform/auth/enrollment", new PlatformEnrollmentRequest(token));
        enrollment.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = (await enrollment.Content.ReadFromJsonAsync<PlatformEnrollmentResponse>(Ct))!;
        body.Uri.ShouldStartWith("otpauth://totp/Auxilia:");

        using var activated = await PostAsync("/api/v1/platform/auth/activate", new PlatformActivateRequest(token, Password, Code(body.Secret, 0)));
        activated.StatusCode.ShouldBe(HttpStatusCode.NoContent, await activated.Content.ReadAsStringAsync(Ct));
        return (email, body.Secret);
    }

    /// <summary>Signs in with the code of the next time step (accepted as clock drift, newer than the activation's).</summary>
    private async Task<TokenResponse> SignInAsync(string email, string secret)
    {
        using var response = await PostTokenAsync(new PlatformTokenRequest("password", email, Password, Code(secret, 1), null));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(Ct))!;
    }

    private async Task<HttpResponseMessage> PostTokenAsync(PlatformTokenRequest body)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/auth/token") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Client-Id", ConsoleClient);
        request.Headers.Add("X-Client-Secret", ConsoleSecret);
        return await client.SendAsync(request, Ct);
    }

    private async Task<HttpResponseMessage> PostAsync<T>(string path, T body)
    {
        using var client = factory.CreateClient();
        return await client.PostAsJsonAsync(path, body, Ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, Ct);
    }

    private static async Task ShouldBeForbiddenAsync(HttpResponseMessage response)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await ErrorCodeAsync(response)).ShouldBe("AUX-12040");
        }
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString();

    /// <summary>RFC 6238 code of the current time step plus <paramref name="stepOffset"/>.</summary>
    internal static string Code(string base32Secret, int stepOffset)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var key = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var character in base32Secret)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(character, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                key.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        var counter = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, (DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30) + stepOffset);
#pragma warning disable CA5350 // RFC 6238 uses HMAC-SHA1.
        var hash = HMACSHA1.HashData(key.ToArray(), counter);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    public sealed class Factory : ApiFactory, IAsyncLifetime
    {
        /// <summary>The daily log files of this host (S-07 reads them back through the Log page API).</summary>
        public string LogRoot { get; } = Path.Combine(Path.GetTempPath(), "auxilia-api-logs-" + Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("AuxiliaLogging:Storage", "local-file");
            builder.UseSetting("AuxiliaLogging:LocalPath", LogRoot);
            builder.UseSetting("AuxiliaLogging:BufferPath", Path.Combine(LogRoot, "buffer"));
            builder.UseSetting("AuxiliaLogging:BatchPeriod", "00:00:00.100");
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            try
            {
                Directory.Delete(LogRoot, recursive: true);
            }
            catch (IOException)
            {
                // The batched sink may still be flushing its last events: the temporary folder is left to the OS.
            }
        }

        protected override IApiEndpoints? Endpoints { get; } = new TechnicalEndpoints();

        protected override IReadOnlyList<IModuleEndpoints> ModuleEndpoints { get; } = [new BusinessEndpoints()];

        public ValueTask InitializeAsync() => new(PlatformTenantTokens.EnsureConsoleClientAsync(Services));

        /// <returns>The activation token (what <c>auxctl platform users add</c> prints).</returns>
        public async Task<string> AddPlatformUserAsync(string email)
        {
            await using var scope = Services.CreateAsyncScope();
            var added = await scope.ServiceProvider.GetRequiredService<IPlatformUserManager>().AddAsync(email, "Ops", CancellationToken.None);
            return added.Value.ActivationToken;
        }
    }

    private sealed class TechnicalEndpoints : IApiEndpoints
    {
        public void Map(RouteGroupBuilder api) =>
            api.MapGet("/test-platform/whoami", (ICurrentUser user, ITenantContext tenant) =>
                    TypedResults.Ok(new { actor = user.ActorType.ToString(), tenant = tenant.Current?.Slug }))
                .RequirePlatformTenant()
                .ExcludeFromDescription();
    }

    private sealed class BusinessEndpoints : IModuleEndpoints
    {
        public string ModuleCode => CasesModule.ModuleCode;

        public void Map(RouteGroupBuilder module) =>
            module.MapGet("/test-business/services", () => TypedResults.Ok("business"))
                .RequirePermission(CasesPermissions.ManageServices)
                .ExcludeFromDescription();
    }
}
