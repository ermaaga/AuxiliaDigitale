using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Host;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Configuration;
using Auxilia.Application.Identity;
using Auxilia.Application.Messaging.Public;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Identity;
using Auxilia.Domain.Identity;
using Auxilia.Persistence.Tenant;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace Auxilia.Api.IntegrationTests.Identity;

/// <summary>F35 over HTTP: password policy, e-mailed sign-in codes, expired and self-service password changes, login audit.</summary>
public sealed class AccountSecurityEndpointsTests : IClassFixture<AccountSecurityEndpointsTests.Factory>
{
    private const string Password = "A long enough Passw0rd!";

    private readonly Factory factory;

    public AccountSecurityEndpointsTests(Factory factory) => this.factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PasswordPolicy_IsPublicAndShowsTheTenantRules()
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/v1/auth/password-policy");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var policy = (await response.Content.ReadFromJsonAsync<PasswordPolicyResponse>(Ct))!;
        policy.ShouldBe(new PasswordPolicyResponse(12, true, true, true, true, 3));
    }

    [Fact]
    public async Task EmailOtp_WhenEnabled_SignsInWithTheMailedCodeOnce()
    {
        await factory.SetTenantSettingAsync(IdentitySettings.OtpLoginEnabled.Key, true);
        var (_, userName) = await factory.AddUserAsync([TenantRole.Client], Password);

        using var methods = await SendAsync(HttpMethod.Get, "/api/v1/auth/methods");
        (await methods.Content.ReadFromJsonAsync<LoginMethodsResponse>(Ct))!.Methods.ShouldBe([LoginMethods.Password, LoginMethods.EmailOtp]);

        using var unknown = await SendAsync(HttpMethod.Post, "/api/v1/auth/otp", new LoginOtpRequest("nobody-" + Guid.NewGuid().ToString("N")[..6]));
        using var requested = await SendAsync(HttpMethod.Post, "/api/v1/auth/otp", new LoginOtpRequest(userName));
        unknown.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        requested.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var message = factory.Messages.Single(item => item.Model["name"] as string == userName);
        message.TemplateCode.ShouldBe(MessageTemplates.LoginOtp);
        var code = (string)message.Model["code"]!;
        code.Length.ShouldBe(6);

        using var wrong = await PostTokenAsync(new TokenRequest("email_otp", userName, null, null, code == "000000" ? "111111" : "000000"));
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var signedIn = await PostTokenAsync(new TokenRequest("email_otp", userName, null, null, code));
        signedIn.StatusCode.ShouldBe(HttpStatusCode.OK, await signedIn.Content.ReadAsStringAsync(Ct));
        (await signedIn.Content.ReadFromJsonAsync<TokenResponse>(Ct))!.AccessToken.ShouldNotBeNullOrEmpty();
        using var reused = await PostTokenAsync(new TokenRequest("email_otp", userName, null, null, code));
        reused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExpiredPassword_Is403UntilChangedThenSignsIn()
    {
        await factory.SetTenantSettingAsync(IdentitySettings.PasswordExpiryEnabled.Key, true);
        var (userId, userName) = await factory.AddUserAsync([TenantRole.Client], Password);
        await Factory.AgePasswordAsync(userId, TimeSpan.FromDays(400));

        using var expired = await PostTokenAsync(new TokenRequest("password", userName, Password, null));
        expired.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(expired)).ShouldBe("AUX-12043");

        using var reused = await SendAsync(HttpMethod.Post, "/api/v1/auth/password/change", new ChangeExpiredPasswordRequest(userName, Password, Password), client: true);
        reused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(reused)).ShouldBe("AUX-12042");

        using var changed = await SendAsync(HttpMethod.Post, "/api/v1/auth/password/change", new ChangeExpiredPasswordRequest(userName, Password, "A fresh expired Passw0rd!"), client: true);
        changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync(Ct));
        (await changed.Content.ReadFromJsonAsync<TokenResponse>(Ct))!.AccessToken.ShouldNotBeNullOrEmpty();
        (await factory.SignInAsync(userName, "A fresh expired Passw0rd!", Ct)).ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task MePassword_ChecksTheCurrentOneAndThePolicy()
    {
        var (_, userName) = await factory.AddUserAsync([TenantRole.Employee], Password);
        var token = await factory.SignInAsync(userName, Password, Ct);
        var other = await factory.SignInAsync(userName, Password, Ct);

        using var wrongCurrent = await SendAsync(HttpMethod.Post, "/api/v1/me/password", new ChangePasswordRequest("not my password", "A changed own Passw0rd!"), token);
        wrongCurrent.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(wrongCurrent)).ShouldBe("AUX-12044");

        using var weak = await SendAsync(HttpMethod.Post, "/api/v1/me/password", new ChangePasswordRequest(Password, "weakpassword"), token);
        weak.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(weak)).ShouldBe("AUX-12005");

        using var changed = await SendAsync(HttpMethod.Post, "/api/v1/me/password", new ChangePasswordRequest(Password, "A changed own Passw0rd!"), token);
        changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The session that changed the password stays valid, the other ones end; the password is the new one.
        using var me = await SendAsync(HttpMethod.Get, "/api/v1/me", accessToken: token);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var ended = await SendAsync(HttpMethod.Get, "/api/v1/me", accessToken: other);
        ended.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ErrorCodeAsync(ended)).ShouldBe("AUX-12023");
        (await factory.SignInAsync(userName, "A changed own Passw0rd!", Ct)).ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task LoginAttempts_AreListedForAdministratorsOnly()
    {
        var (_, admin) = await factory.AddUserAsync([TenantRole.Administrator], Password);
        var (_, employee) = await factory.AddUserAsync([TenantRole.Employee], Password);
        using (await PostTokenAsync(new TokenRequest("password", employee, "a wrong Passw0rd!", null)))
        {
        }

        var adminToken = await factory.SignInAsync(admin, Password, Ct);
        var employeeToken = await factory.SignInAsync(employee, Password, Ct);

        using var forbidden = await SendAsync(HttpMethod.Get, "/api/v1/identity/login-attempts", accessToken: employeeToken);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var listed = await SendAsync(HttpMethod.Get, $"/api/v1/identity/login-attempts?filter[userName]={employee}&sort=attemptedAt", accessToken: adminToken);
        listed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = (await listed.Content.ReadFromJsonAsync<PagedResponse<LoginAttemptResponse>>(Ct))!;
        page.TotalCount.ShouldBe(2);
        page.Items.Select(item => (item.Succeeded, item.FailureReason, item.Method))
            .ShouldBe([(false, "InvalidCredentials", LoginMethods.Password), (true, null, LoginMethods.Password)]);

        using var invalid = await SendAsync(HttpMethod.Get, "/api/v1/identity/login-attempts?pageSize=500", accessToken: adminToken);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private Task<HttpResponseMessage> PostTokenAsync(TokenRequest body) => SendAsync(HttpMethod.Post, "/api/v1/auth/token", body, client: true);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, string? accessToken = null, bool client = false)
    {
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        if (accessToken is null)
        {
            // Anonymous calls name the tenant; a bearer token carries it.
            request.Headers.Add("X-Tenant", ApiDatabase.TenantA);
        }

        if (client)
        {
            request.Headers.Add("X-Client-Id", AuthEndpointsTests.ClientId);
            request.Headers.Add("X-Client-Secret", factory.ClientSecret);
        }

        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await http.SendAsync(request, Ct);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString();

    public sealed class Factory : AuthEndpointsTests.Factory
    {
        /// <summary>Stores a tenant-level value for tenant A (the settings cache is invalidated after commit).</summary>
        public async Task SetTenantSettingAsync<T>(string key, T value)
        {
            await using var scope = Services.CreateAsyncScope();
            var tenants = scope.ServiceProvider.GetRequiredService<ITenantDirectory>();
            scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().Set((await tenants.FindBySlugAsync(ApiDatabase.TenantA, CancellationToken.None))!);
            var result = await scope.ServiceProvider.GetRequiredService<ISettingsManager>()
                .SetAsync(new SetSetting(key, SettingScope.Tenant, JsonSerializer.SerializeToElement(value)), CancellationToken.None);
            result.IsSuccess.ShouldBeTrue(result.Error?.Description);
        }

        /// <summary>Moves the last password change of the user back by <paramref name="age"/>.</summary>
        public static async Task AgePasswordAsync(Guid userId, TimeSpan age)
        {
            var connectionString = new NpgsqlConnectionStringBuilder(ApiDatabase.Instance.CatalogConnectionString) { Database = "tenant_a" }.ConnectionString;
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await using var db = new TenantDbContext(TenantDbContextOptions.Create(dataSource));
            var user = await db.Set<User>().SingleAsync(item => item.Id == userId);
            user.SetPassword(user.PasswordHash!, user.PasswordFormat, DateTimeOffset.UtcNow - age);
            await db.SaveChangesAsync();
        }
    }
}
