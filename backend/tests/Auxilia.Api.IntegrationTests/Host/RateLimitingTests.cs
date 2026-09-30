using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Contracts.Identity;

using Microsoft.AspNetCore.Hosting;

namespace Auxilia.Api.IntegrationTests.Host;

/// <summary>Rate limits (skill auxilia-security): 429 ProblemDetails with Retry-After, per tenant, health not limited.</summary>
public sealed class RateLimitingTests : IClassFixture<RateLimitingTests.Factory>
{
    private readonly Factory factory;

    public RateLimitingTests(Factory factory) => this.factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SignIn_OverTheLimit_Is429WithRetryAfter_AndOtherTenantsKeepTheirBudget()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var allowed = await PostTokenAsync(ApiDatabase.TenantA);
            allowed.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        }

        using var limited = await PostTokenAsync(ApiDatabase.TenantA);
        using var otherTenant = await PostTokenAsync(ApiDatabase.TenantB);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.ShouldNotBeNull();
        var problem = await limited.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("errorCode").GetString().ShouldBe("AUX-10024");
        problem.GetProperty("status").GetInt32().ShouldBe(429);
        otherTenant.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task AccountLinks_HaveTheirOwnStricterBudget()
    {
        using var first = await PostForgotAsync();
        using var second = await PostForgotAsync();
        using var third = await PostForgotAsync();

        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        second.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task AuthenticatedUser_HasABudgetPerTenantAndUser()
    {
        var responses = new List<HttpStatusCode>();
        var userId = Guid.CreateVersion7().ToString();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/no-such-route");
            request.Headers.Add(ApiFactory.TestTenantClaimHeader, ApiDatabase.TenantA);
            request.Headers.Add(ApiFactory.TestUserHeader, userId);
            using var response = await client.SendAsync(request, Ct);
            responses.Add(response.StatusCode);
        }

        responses.ShouldBe([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests]);
    }

    [Fact]
    public async Task HealthEndpoints_AreNeverLimited()
    {
        using var client = factory.CreateClient();
        for (var attempt = 0; attempt < 30; attempt++)
        {
            using var response = await client.GetAsync("/health/live", Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    private async Task<HttpResponseMessage> PostTokenAsync(string tenant)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/token")
        {
            Content = JsonContent.Create(new TokenRequest("password", "nobody", "not the password", null)),
        };
        request.Headers.Add("X-Tenant", tenant);
        request.Headers.Add("X-Client-Id", "rate-limit-tests");
        return await client.SendAsync(request, Ct);
    }

    private async Task<HttpResponseMessage> PostForgotAsync()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/password/forgot")
        {
            Content = JsonContent.Create(new ForgotPasswordRequest("nobody")),
        };
        request.Headers.Add("X-Tenant", ApiDatabase.TenantB);
        return await client.SendAsync(request, Ct);
    }

    public sealed class Factory : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("RateLimiting:Enabled", "true");
            builder.UseSetting("RateLimiting:SignIn:PermitLimit", "3");
            builder.UseSetting("RateLimiting:SignIn:Window", "00:10:00");
            builder.UseSetting("RateLimiting:AccountLinks:PermitLimit", "2");
            builder.UseSetting("RateLimiting:AccountLinks:Window", "00:10:00");
            builder.UseSetting("RateLimiting:User:PermitLimit", "4");
            builder.UseSetting("RateLimiting:User:Window", "00:10:00");
        }
    }
}
