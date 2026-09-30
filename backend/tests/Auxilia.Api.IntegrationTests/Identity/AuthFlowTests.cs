using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Host;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Identity;
using Auxilia.Contracts.Identity;
using Auxilia.Domain.Identity;
using Auxilia.Persistence.Tenant;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace Auxilia.Api.IntegrationTests.Identity;

/// <summary>
/// P2-07: sign-in flows over HTTP not covered elsewhere — the designed <c>external_code</c> grant, refresh tokens across
/// tenants, lockout, disabled accounts and the operator's temporary password (F01, F31).
/// </summary>
public sealed class AuthFlowTests : IClassFixture<AccountSecurityEndpointsTests.Factory>
{
    private const string Password = "A long enough Passw0rd!";

    private readonly AccountSecurityEndpointsTests.Factory factory;

    public AuthFlowTests(AccountSecurityEndpointsTests.Factory factory) => this.factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExternalCodeGrant_IsDesignedButNotEnabled()
    {
        using var response = await PostTokenAsync(new TokenRequest("external_code", null, null, null, "provider-code"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(response)).ShouldBe("AUX-12045");
    }

    [Fact]
    public async Task RefreshToken_OfAnotherTenant_IsRejected()
    {
        var (_, userName) = await factory.AddUserAsync([TenantRole.Employee], Password);
        var tokens = await SignInAsync(userName, Password);

        using var otherTenant = await PostTokenAsync(new TokenRequest("refresh_token", null, null, tokens.RefreshToken), ApiDatabase.TenantB);
        otherTenant.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ErrorCodeAsync(otherTenant)).ShouldBe("AUX-12015");

        // Still valid in its own tenant.
        using var ownTenant = await PostTokenAsync(new TokenRequest("refresh_token", null, null, tokens.RefreshToken));
        ownTenant.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RepeatedWrongPasswords_LockTheAccount_EvenForTheRightPassword()
    {
        var (_, userName) = await factory.AddUserAsync([TenantRole.Client], Password);

        for (var attempt = 0; attempt < IdentitySettings.LockoutMaxFailedAttempts.Default; attempt++)
        {
            using var wrong = await PostTokenAsync(new TokenRequest("password", userName, "Wrong Passw0rd!", null));
            wrong.StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        }

        using var locked = await PostTokenAsync(new TokenRequest("password", userName, Password, null));
        locked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(locked)).ShouldBe("AUX-12003");
    }

    [Fact]
    public async Task DisabledAccount_CannotSignInNorRefresh()
    {
        var (userId, userName) = await factory.AddUserAsync([TenantRole.Client], Password);
        var tokens = await SignInAsync(userName, Password);

        await ChangeUserAsync(userId, user => user.SetActive(false));

        using var signIn = await PostTokenAsync(new TokenRequest("password", userName, Password, null));
        signIn.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var refresh = await PostTokenAsync(new TokenRequest("refresh_token", null, null, tokens.RefreshToken));
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OperatorTemporaryPassword_EndsTheSessionsAndForcesAChange()
    {
        var (_, userName) = await factory.AddUserAsync([TenantRole.Client], Password);
        var before = await SignInAsync(userName, Password);

        string temporary;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tenant = await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().FindBySlugAsync(ApiDatabase.TenantA, Ct);
            scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().Set(tenant!);
            var reset = await scope.ServiceProvider.GetRequiredService<IAccountLinkManager>().ResetPasswordByOperatorAsync(userName, sendLink: false, Ct);
            temporary = reset.Value.TemporaryPassword!;
        }

        using var oldSession = await SendAsync(HttpMethod.Get, "/api/v1/me", before.AccessToken);
        oldSession.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var mustChange = await PostTokenAsync(new TokenRequest("password", userName, temporary, null));
        mustChange.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(mustChange)).ShouldBe("AUX-12043");

        using var changed = await PostAsync("/api/v1/auth/password/change", new ChangeExpiredPasswordRequest(userName, temporary, "My own new Passw0rd!"));
        changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync(Ct));
        (await SignInAsync(userName, "My own new Passw0rd!")).AccessToken.ShouldNotBeNullOrEmpty();
    }

    private async Task<TokenResponse> SignInAsync(string userName, string password)
    {
        using var response = await PostTokenAsync(new TokenRequest("password", userName, password, null));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(Ct))!;
    }

    private Task<HttpResponseMessage> PostTokenAsync(TokenRequest body, string tenant = ApiDatabase.TenantA) => PostAsync("/api/v1/auth/token", body, tenant);

    private async Task<HttpResponseMessage> PostAsync<T>(string path, T body, string tenant = ApiDatabase.TenantA)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Tenant", tenant);
        request.Headers.Add("X-Client-Id", AuthEndpointsTests.ClientId);
        request.Headers.Add("X-Client-Secret", factory.ClientSecret);
        return await client.SendAsync(request, Ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string accessToken)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(request, Ct);
    }

    private static async Task ChangeUserAsync(Guid userId, Action<User> change)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(ApiDatabase.Instance.CatalogConnectionString) { Database = "tenant_a" }.ConnectionString;
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var db = new TenantDbContext(TenantDbContextOptions.Create(dataSource));
        change(await db.Set<User>().SingleAsync(item => item.Id == userId));
        await db.SaveChangesAsync();
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString();
}
