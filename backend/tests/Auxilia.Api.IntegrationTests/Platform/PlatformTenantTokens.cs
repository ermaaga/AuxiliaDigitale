using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Identity;
using Auxilia.Contracts.Identity;
using Auxilia.Contracts.Platform;
using Auxilia.Domain.Platform;
using Auxilia.Persistence.Catalog;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Api.IntegrationTests.Platform;

/// <summary>A new System user, activated with TOTP and signed in on the console client, opens a tenant (D-21).</summary>
internal static class PlatformTenantTokens
{
    public const string ConsoleClient = "test-console";
    public const string ConsoleSecret = "integration-tests-console-credential";
    private const string Password = "a long enough platform password";

    private static readonly SemaphoreSlim ClientLock = new(1, 1);

    public static Task<string> IssueAsync(PlatformIdentityTests.Factory factory, string tenant, CancellationToken cancellationToken) =>
        IssueAsync((WebApplicationFactory<Program>)factory, tenant, cancellationToken);

    /// <summary>A tenant-scoped platform token for <paramref name="tenant"/> (needs <see cref="EnsureConsoleClientAsync"/>).</summary>
    public static async Task<string> IssueAsync(WebApplicationFactory<Program> factory, string tenant, CancellationToken cancellationToken)
    {
        var console = await SignInConsoleAsync(factory, cancellationToken);
        using var client = factory.CreateClient();
        using var open = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/platform/tenants/{tenant}/token");
        open.Headers.Authorization = new AuthenticationHeaderValue("Bearer", console);
        using var opened = await client.SendAsync(open, cancellationToken);
        return (await opened.Content.ReadFromJsonAsync<PlatformTenantTokenResponse>(cancellationToken))!.AccessToken;
    }

    /// <summary>A console token (platform, no tenant) of a new System user (needs <see cref="EnsureConsoleClientAsync"/>).</summary>
    public static async Task<string> SignInConsoleAsync(WebApplicationFactory<Program> factory, CancellationToken cancellationToken)
    {
        var email = "ops-" + Guid.NewGuid().ToString("N")[..8] + "@example.test";
        string activation;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            activation = (await scope.ServiceProvider.GetRequiredService<IPlatformUserManager>().AddAsync(email, "Ops", cancellationToken)).Value.ActivationToken;
        }

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
        signedIn.StatusCode.ShouldBe(HttpStatusCode.OK, await signedIn.Content.ReadAsStringAsync(cancellationToken));
        return (await signedIn.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken))!.AccessToken;
    }

    /// <summary>Registers the console client application (test classes run in parallel and share its row).</summary>
    public static async Task EnsureConsoleClientAsync(IServiceProvider services)
    {
        await ClientLock.WaitAsync();
        try
        {
            var options = new DbContextOptionsBuilder<CatalogDbContext>();
            CatalogPersistence.Configure(options, ApiDatabase.Instance.CatalogConnectionString);
            await using var catalog = new CatalogDbContext(options.Options);
            var client = await catalog.ClientApplications.SingleOrDefaultAsync(item => item.ClientId == ConsoleClient);
            if (client is null)
            {
                client = ClientApplication.Create(Guid.CreateVersion7(), ConsoleClient, "Test console", ClientApplicationType.PlatformConsole).Value;
                catalog.ClientApplications.Add(client);
            }

            client.SetSecretHash(services.GetRequiredService<IPasswordHasher>().Hash(ConsoleSecret));
            await catalog.SaveChangesAsync();
        }
        finally
        {
            ClientLock.Release();
        }
    }
}
