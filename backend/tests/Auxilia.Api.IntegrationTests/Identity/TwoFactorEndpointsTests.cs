using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Host;
using Auxilia.Api.IntegrationTests.Platform;
using Auxilia.Contracts.Identity;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Api.IntegrationTests.Identity;

/// <summary>N04 over HTTP: the own authenticator app, password + code at sign-in, "stay signed in", Administrator reset.</summary>
public sealed class TwoFactorEndpointsTests : IClassFixture<AccountSecurityEndpointsTests.Factory>
{
    private const string Password = "A long enough Passw0rd!";

    private readonly AccountSecurityEndpointsTests.Factory factory;

    public TwoFactorEndpointsTests(AccountSecurityEndpointsTests.Factory factory) => this.factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheApp_IsEnrolledFromTheProfile_AskedAtSignIn_AndResetByAnAdministrator()
    {
        var (userId, userName) = await factory.AddUserAsync([TenantRole.Employee], Password);
        var (_, adminName) = await factory.AddUserAsync([TenantRole.Administrator], Password);
        var token = await SignInAsync(new TokenRequest("password", userName, Password, null));

        using var begun = await SendAsync(HttpMethod.Post, "/api/v1/me/two-factor/enrollment", accessToken: token.AccessToken);
        begun.StatusCode.ShouldBe(HttpStatusCode.OK);
        begun.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var enrollment = (await begun.Content.ReadFromJsonAsync<TwoFactorEnrollmentResponse>(Ct))!;
        enrollment.Uri.ShouldStartWith("otpauth://totp/");
        enrollment.Uri.ShouldContain(userName);

        var current = PlatformIdentityTests.Code(enrollment.Secret, 0);
        using var wrongConfirm = await SendAsync(HttpMethod.Post, "/api/v1/me/two-factor/confirm",
            new ConfirmTwoFactorRequest(current == "000000" ? "111111" : "000000"), token.AccessToken);
        (await ErrorCodeAsync(wrongConfirm)).ShouldBe("AUX-12073");
        using var confirmed = await SendAsync(HttpMethod.Post, "/api/v1/me/two-factor/confirm", new ConfirmTwoFactorRequest(current), token.AccessToken);
        confirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var status = await SendAsync(HttpMethod.Get, "/api/v1/me/two-factor", accessToken: token.AccessToken);
        (await status.Content.ReadFromJsonAsync<TwoFactorStatusResponse>(Ct))!.Enabled.ShouldBeTrue();

        using var withoutCode = await SendAsync(HttpMethod.Post, "/api/v1/auth/token", new TokenRequest("password", userName, Password, null), client: true);
        withoutCode.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ErrorCodeAsync(withoutCode)).ShouldBe("AUX-12072");

        // The next step: the code of the enrolment's step cannot be used again.
        var signedIn = await SignInAsync(new TokenRequest(
            "password", userName, Password, null, TwoFactorCode: PlatformIdentityTests.Code(enrollment.Secret, 1), RememberMe: true));
        signedIn.SessionExpiresIn.ShouldNotBeNull().ShouldBeGreaterThan(86_400);
        token.SessionExpiresIn.ShouldBeNull();

        var admin = await SignInAsync(new TokenRequest("password", adminName, Password, null));
        using var forbidden = await SendAsync(HttpMethod.Delete, $"/api/v1/identity/users/{userId}/two-factor", accessToken: signedIn.AccessToken);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var reset = await SendAsync(HttpMethod.Delete, $"/api/v1/identity/users/{userId}/two-factor", accessToken: admin.AccessToken);
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var ended = await SendAsync(HttpMethod.Get, "/api/v1/me", accessToken: signedIn.AccessToken);
        ended.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SignInAsync(new TokenRequest("password", userName, Password, null))).AccessToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Disable_NeedsThePassword_AndTheRequiredSetupIsRefusedWhenNotRequired()
    {
        var (_, userName) = await factory.AddUserAsync([TenantRole.Client], Password);
        var token = await SignInAsync(new TokenRequest("password", userName, Password, null));
        using var begun = await SendAsync(HttpMethod.Post, "/api/v1/me/two-factor/enrollment", accessToken: token.AccessToken);
        var enrollment = (await begun.Content.ReadFromJsonAsync<TwoFactorEnrollmentResponse>(Ct))!;
        using (await SendAsync(HttpMethod.Post, "/api/v1/me/two-factor/confirm",
            new ConfirmTwoFactorRequest(PlatformIdentityTests.Code(enrollment.Secret, 0)), token.AccessToken))
        {
        }

        using var wrong = await SendAsync(HttpMethod.Post, "/api/v1/me/two-factor/disable", new DisableTwoFactorRequest("not my password"), token.AccessToken);
        (await ErrorCodeAsync(wrong)).ShouldBe("AUX-12044");
        using var disabled = await SendAsync(HttpMethod.Post, "/api/v1/me/two-factor/disable", new DisableTwoFactorRequest(Password), token.AccessToken);
        disabled.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var setup = await SendAsync(HttpMethod.Post, "/api/v1/auth/two-factor/setup", new TwoFactorSetupRequest(userName, Password), client: true);
        setup.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(setup)).ShouldBe("AUX-12075");
        using var noClient = await SendAsync(HttpMethod.Post, "/api/v1/auth/two-factor/setup", new TwoFactorSetupRequest(userName, Password));
        (await ErrorCodeAsync(noClient)).ShouldBe("AUX-12016");
    }

    [Fact]
    public async Task Methods_TellTheDaysOfStaySignedIn()
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/v1/auth/methods");

        (await response.Content.ReadFromJsonAsync<LoginMethodsResponse>(Ct))!.RememberMeDays.ShouldBe(14);
    }

    private async Task<TokenResponse> SignInAsync(TokenRequest request)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/auth/token", request, client: true);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(Ct))!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, string? accessToken = null, bool client = false)
    {
        var http = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        if (accessToken is null)
        {
            request.Headers.Add("X-Tenant", ApiDatabase.TenantA);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (client)
        {
            request.Headers.Add("X-Client-Id", AuthEndpointsTests.ClientId);
            request.Headers.Add("X-Client-Secret", factory.ClientSecret);
        }

        return await http.SendAsync(request, Ct);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        response.Content.Headers.ContentLength == 0 || response.StatusCode == HttpStatusCode.NoContent
            ? null
            : (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString();
}
