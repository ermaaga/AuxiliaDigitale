using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Auxilia.Contracts.Identity;
using Auxilia.Contracts.Platform;

namespace Auxilia.Api.IntegrationTests.Platform;

/// <summary>A new System user, activated with TOTP and signed in on the console client, opens a tenant (D-21).</summary>
internal static class PlatformTenantTokens
{
    private const string ConsoleClient = "test-console";
    private const string ConsoleSecret = "integration-tests-console-credential";
    private const string Password = "a long enough platform password";

    public static async Task<string> IssueAsync(PlatformIdentityTests.Factory factory, string tenant, CancellationToken cancellationToken)
    {
        var email = "ops-" + Guid.NewGuid().ToString("N")[..8] + "@example.test";
        var activation = await factory.AddPlatformUserAsync(email);
        using var client = factory.CreateClient();

        using var enrollment = await client.PostAsJsonAsync("/api/v1/platform/auth/enrollment", new PlatformEnrollmentRequest(activation), cancellationToken);
        var secret = (await enrollment.Content.ReadFromJsonAsync<PlatformEnrollmentResponse>(cancellationToken))!.Secret;
        using var activated = await client.PostAsJsonAsync(
            "/api/v1/platform/auth/activate", new PlatformActivateRequest(activation, Password, PlatformIdentityTests.Code(secret, 0)), cancellationToken);
        activated.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var signIn = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/auth/token")
        {
            Content = JsonContent.Create(new PlatformTokenRequest("password", email, Password, PlatformIdentityTests.Code(secret, 1), null)),
        };
        signIn.Headers.Add("X-Client-Id", ConsoleClient);
        signIn.Headers.Add("X-Client-Secret", ConsoleSecret);
        using var signedIn = await client.SendAsync(signIn, cancellationToken);
        var console = (await signedIn.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken))!.AccessToken;

        using var open = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/platform/tenants/{tenant}/token");
        open.Headers.Authorization = new AuthenticationHeaderValue("Bearer", console);
        using var opened = await client.SendAsync(open, cancellationToken);
        return (await opened.Content.ReadFromJsonAsync<PlatformTenantTokenResponse>(cancellationToken))!.AccessToken;
    }
}
