using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.Endpoints;
using Auxilia.Api.IntegrationTests.Host;
using Auxilia.Api.Tenancy;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Application.Abstractions.Tenancy;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Auxilia.Api.IntegrationTests.Tenancy;

/// <summary>Tenant resolution, status gate and database isolation (skill auxilia-multitenancy).</summary>
public sealed class TenantResolutionTests : IClassFixture<TenantResolutionTests.Factory>
{
    private readonly Factory factory;

    public TenantResolutionTests(Factory factory) => this.factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Header_ResolvesTenant()
    {
        using var response = await SendAsync("/api/v1/test/tenant", header: ApiDatabase.TenantA);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe($"\"{ApiDatabase.TenantA}\"");
    }

    [Fact]
    public async Task Header_IsCaseInsensitive()
    {
        using var response = await SendAsync("/api/v1/test/tenant", header: "TENANT-A");

        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe($"\"{ApiDatabase.TenantA}\"");
    }

    [Fact]
    public async Task Subdomain_ResolvesTenant()
    {
        using var response = await SendAsync("/api/v1/test/tenant", host: $"{ApiDatabase.TenantB}.{ApiFactory.BaseDomain}");

        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe($"\"{ApiDatabase.TenantB}\"");
    }

    [Fact]
    public async Task CustomDomain_ResolvesTenant()
    {
        using var response = await SendAsync("/api/v1/test/tenant", host: ApiDatabase.TenantBCustomHost);

        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe($"\"{ApiDatabase.TenantB}\"");
    }

    [Fact]
    public async Task Claim_ResolvesTenantWithoutHeader()
    {
        using var response = await SendAsync("/api/v1/test/tenant", claim: ApiDatabase.TenantA);

        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe($"\"{ApiDatabase.TenantA}\"");
    }

    [Theory]
    [InlineData(ApiDatabase.TenantB, null)]
    [InlineData(null, "tenant-b.auxilia.test")]
    public async Task ClaimDisagreeingWithHeaderOrHost_Returns403CrossTenant(string? header, string? host)
    {
        using var response = await SendAsync("/api/v1/test/tenant", header: header, host: host, claim: ApiDatabase.TenantA);

        await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, "AUX-11004");
    }

    [Theory]
    [InlineData("unknown-tenant")]
    [InlineData("../etc")]
    public async Task UnknownTenant_Returns404(string header)
    {
        using var response = await SendAsync("/api/v1/test/tenant", header: header);

        await ShouldBeProblemAsync(response, HttpStatusCode.NotFound, "AUX-11009");
    }

    [Fact]
    public async Task NoTenant_OnTenantEndpoint_Returns400()
    {
        using var response = await SendAsync("/api/v1/test/tenant");

        await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, "AUX-11008");
    }

    [Fact]
    public async Task NoTenant_OnEndpointWithoutTenant_IsServed()
    {
        using var response = await SendAsync("/api/v1/test/any");

        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("\"none\"");
    }

    [Theory]
    [InlineData(ApiDatabase.Suspended, HttpStatusCode.Locked, "AUX-11011")]
    [InlineData(ApiDatabase.Provisioning, HttpStatusCode.ServiceUnavailable, "AUX-11010")]
    [InlineData(ApiDatabase.Archived, HttpStatusCode.NotFound, "AUX-11009")]
    public async Task InactiveTenant_IsNotServedByTenantEndpoints(string slug, HttpStatusCode status, string errorCode)
    {
        using var response = await SendAsync("/api/v1/test/tenant", header: slug);

        await ShouldBeProblemAsync(response, status, errorCode);
    }

    [Fact]
    public async Task InactiveTenant_IsStillResolvedForEndpointsWithoutTheGate()
    {
        // Platform endpoints (System console) operate on suspended tenants.
        using var response = await SendAsync("/api/v1/test/any", header: ApiDatabase.Suspended);

        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe($"\"{ApiDatabase.Suspended}\"");
    }

    [Theory]
    [InlineData(ApiDatabase.TenantA, "tenant_a")]
    [InlineData(ApiDatabase.TenantB, "tenant_b")]
    public async Task TenantDbContextFactory_ConnectsToTheTenantDatabase(string slug, string database)
    {
        using var response = await SendAsync("/api/v1/test/database", header: slug);

        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe($"\"{database}\"");
    }

    private async Task<HttpResponseMessage> SendAsync(string path, string? header = null, string? host = null, string? claim = null)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{host ?? "localhost"}"),
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (header is not null)
        {
            request.Headers.Add(TenantResolutionMiddleware.TenantHeader, header);
        }

        if (claim is not null)
        {
            request.Headers.Add(ApiFactory.TestTenantClaimHeader, claim);
        }

        return await client.SendAsync(request, Ct);
    }

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        response.StatusCode.ShouldBe(status);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("errorCode").GetString().ShouldBe(errorCode);
    }

    public sealed class Factory : ApiFactory
    {
        protected override IApiEndpoints Endpoints { get; } = new TestEndpoints();
    }

    private sealed class TestEndpoints : IApiEndpoints
    {
        public void Map(RouteGroupBuilder api)
        {
            var test = api.MapGroup("/test");
            test.MapGet("/tenant", (ITenantContext tenant) => TypedResults.Ok(tenant.Tenant.Slug)).RequireTenant();
            test.MapGet("/any", (ITenantContext tenant) => TypedResults.Ok(tenant.Current?.Slug ?? "none"));
            test.MapGet("/database", async (ITenantDbContextFactory databases, CancellationToken cancellationToken) =>
            {
                await using var db = await databases.CreateAsync(cancellationToken);
                var name = await ((DbContext)db).Database.SqlQueryRaw<string>("select current_database() as \"Value\"").SingleAsync(cancellationToken);
                return TypedResults.Ok(name);
            }).RequireTenant();
        }
    }
}
