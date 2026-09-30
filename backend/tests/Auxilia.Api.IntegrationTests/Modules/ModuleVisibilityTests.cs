using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.Endpoints;
using Auxilia.Api.IntegrationTests.Host;
using Auxilia.Application.Abstractions.Caching;
using Auxilia.Persistence.Catalog;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Api.IntegrationTests.Modules;

/// <summary>Module endpoints exist only where the module is visible to the caller's roles (ARCHITECTURE §5.2, F22, N02).</summary>
public sealed class ModuleVisibilityTests : IClassFixture<ModuleVisibilityTests.Factory>
{
    private readonly Factory factory;

    public ModuleVisibilityTests(Factory factory) => this.factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Employee")]
    [InlineData("Administrator")]
    [InlineData("Client,Employee")]
    public async Task OptionalModule_InThePlanForTheRole_Answers(string roles)
    {
        using var response = await SendAsync("/api/v1/test-cases/ping", ApiDatabase.TenantA, roles);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(ApiDatabase.TenantA, "Client")]
    [InlineData(ApiDatabase.TenantA, null)]
    [InlineData(ApiDatabase.TenantB, "Administrator")]
    public async Task OptionalModule_NotVisible_IsIndistinguishableFromAMissingRoute(string tenant, string? roles)
    {
        using var response = await SendAsync("/api/v1/test-cases/ping", tenant, roles);
        using var missing = await SendAsync("/api/v1/no-such-route", tenant, roles);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var missingProblem = await missing.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("errorCode").GetString().ShouldBe("AUX-10017");
        problem.GetProperty("title").GetString().ShouldBe(missingProblem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task CoreModule_IsVisibleToEveryRole()
    {
        using var response = await SendAsync("/api/v1/test-identity/ping", ApiDatabase.TenantB, "Client");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TenantStatus_IsCheckedBeforeTheModule()
    {
        using var response = await SendAsync("/api/v1/test-cases/ping", ApiDatabase.Suspended, "Administrator");

        response.StatusCode.ShouldBe((HttpStatusCode)StatusCodes.Status423Locked);
    }

    [Fact]
    public async Task OverrideChange_TakesEffectAfterInvalidation()
    {
        using (var before = await SendAsync("/api/v1/test-identity/ping", ApiDatabase.TenantB, "Employee"))
        {
            before.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await SetOverrideAsync(isEnabled: true, [TenantRole.Employee]);
        try
        {
            // Cached until the System's change invalidates the tenant's modules (S-01 does it after commit).
            (await SendAsync("/api/v1/test-cases/ping", ApiDatabase.TenantB, "Employee")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

            await factory.Services.GetRequiredService<IReferenceDataCache>().InvalidateAsync(CacheTags.Tenant(ApiDatabase.TenantB, "platform"), Ct);

            (await SendAsync("/api/v1/test-cases/ping", ApiDatabase.TenantB, "Employee")).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await SendAsync("/api/v1/test-cases/ping", ApiDatabase.TenantB, "Administrator")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        finally
        {
            await SetOverrideAsync(isEnabled: false, []);
            await factory.Services.GetRequiredService<IReferenceDataCache>().InvalidateAsync(CacheTags.Tenant(ApiDatabase.TenantB, "platform"), Ct);
        }
    }

    private static async Task SetOverrideAsync(bool isEnabled, TenantRole[] roles)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        CatalogPersistence.Configure(options, ApiDatabase.Instance.CatalogConnectionString);
        await using var catalog = new CatalogDbContext(options.Options);
        var tenantId = await catalog.Tenants.Where(tenant => tenant.Slug == ApiDatabase.TenantB).Select(tenant => tenant.Id).SingleAsync(Ct);
        var item = await catalog.TenantModuleOverrides.SingleAsync(entry => entry.TenantId == tenantId && entry.ModuleCode == "cases", Ct);
        item.Set(isEnabled, roles);
        await catalog.SaveChangesAsync(Ct);
    }

    private async Task<HttpResponseMessage> SendAsync(string path, string tenant, string? roles)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Tenant", tenant);
        request.Headers.Add(ApiFactory.TestTenantClaimHeader, tenant);
        if (roles is not null)
        {
            request.Headers.Add(ApiFactory.TestRolesHeader, roles);
        }

        return await client.SendAsync(request, Ct);
    }

    public sealed class Factory : ApiFactory
    {
        protected override IReadOnlyList<IModuleEndpoints> ModuleEndpoints { get; } =
            [new PingEndpoints("cases", "/test-cases"), new PingEndpoints("identity", "/test-identity")];
    }

    private sealed class PingEndpoints(string moduleCode, string prefix) : IModuleEndpoints
    {
        public string ModuleCode => moduleCode;

        public void Map(RouteGroupBuilder module) => module.MapGet(prefix + "/ping", () => TypedResults.Ok("pong"));
    }
}
