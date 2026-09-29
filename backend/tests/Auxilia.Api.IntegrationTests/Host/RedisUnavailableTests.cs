using System.Net;

using Auxilia.Api.Endpoints;
using Auxilia.Api.Tenancy;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Cases;
using Auxilia.ServiceDefaults;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Auxilia.Api.IntegrationTests.Host;

/// <summary>With Redis unreachable the API keeps answering from memory and the database; readiness reports Degraded.</summary>
public sealed class RedisUnavailableTests : IClassFixture<RedisUnavailableTests.Factory>
{
    private readonly Factory factory;

    public RedisUnavailableTests(Factory factory) => this.factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TenantRequest_ReadsSettingsAndSucceeds()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/setting");
        request.Headers.Add("X-Tenant", ApiDatabase.TenantA);

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("7");
    }

    [Fact]
    public async Task Readiness_IsDegraded()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(ServiceDefaultsExtensions.ReadyPath, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("Degraded");
    }

    public sealed class Factory : ApiFactory
    {
        protected override IApiEndpoints Endpoints { get; } = new SettingEndpoints();

        protected override string RedisConnectionString => "127.0.0.1:1,connectTimeout=500";
    }

    private sealed class SettingEndpoints : IApiEndpoints
    {
        public void Map(RouteGroupBuilder api) =>
            api.MapGet("/test/setting", async (ISettingsProvider settings, CancellationToken cancellationToken) =>
                TypedResults.Ok(await settings.GetAsync(CasesSettings.ExpiryExpiringDays, cancellationToken))).RequireTenant();
    }
}
