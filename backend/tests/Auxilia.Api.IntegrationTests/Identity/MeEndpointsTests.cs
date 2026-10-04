using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.Authorization;
using Auxilia.Api.Endpoints;
using Auxilia.Application.Cases;
using Auxilia.Application.Identity;
using Auxilia.Contracts.Identity;
using Auxilia.Contracts.Platform;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Auxilia.Api.IntegrationTests.Identity;

/// <summary>F22 over HTTP: <c>/me</c>, <c>/me/navigation</c> and permission-guarded module endpoints (403 vs 404).</summary>
public sealed class MeEndpointsTests : IClassFixture<MeEndpointsTests.Factory>
{
    private const string Password = "a long enough password";

    private readonly Factory factory;

    public MeEndpointsTests(Factory factory) => this.factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Me_ReturnsTheUserRolesAndEffectivePermissions()
    {
        var (userId, userName) = await factory.AddUserAsync([TenantRole.Employee], Password);
        var token = await factory.SignInAsync(userName, Password, Ct);

        using var response = await GetAsync("/api/v1/me", token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var me = (await response.Content.ReadFromJsonAsync<MeResponse>(Ct))!;
        (me.Id, me.UserName, me.Tenant, me.Language).ShouldBe((userId, userName, ApiDatabase.TenantA, "it"));
        me.Roles.ShouldBe(["Employee"]);
        me.Permissions.ShouldContain(CasesPermissions.ManageCases);
        me.Permissions.ShouldNotContain(CasesPermissions.ManageServices);
        me.Permissions.ShouldNotContain(IdentityPermissions.ViewSessions);

        // Only the modules of the tenant's catalog grant permissions.
        me.Permissions.ShouldAllBe(permission => permission.StartsWith("cases.", StringComparison.Ordinal)
            || permission.StartsWith("identity.", StringComparison.Ordinal)
            || permission.StartsWith("directory.", StringComparison.Ordinal)
            || permission.StartsWith("documents.", StringComparison.Ordinal)
            || permission.StartsWith("scheduling.", StringComparison.Ordinal)
            || permission.StartsWith("engagement.", StringComparison.Ordinal)
            || permission.StartsWith("reporting.", StringComparison.Ordinal)
            || permission.StartsWith("marketing.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Administrator", new[] { "dashboard", "clients", "employees", "cases", "services", "appointments", "documents", "requests", "tasks", "marketing", "sessions", "loginAudit" })]
    [InlineData("Employee", new[] { "dashboard", "clients", "cases", "appointments", "documents", "requests", "tasks", "marketing" })]
    [InlineData("Client", new[] { "dashboard", "appointments", "requests" })]
    public async Task Navigation_FollowsModulesRolesAndPermissions(string role, string[] keys)
    {
        var (_, userName) = await factory.AddUserAsync([Enum.Parse<TenantRole>(role)], Password);
        var token = await factory.SignInAsync(userName, Password, Ct);

        var items = (await GetJsonAsync<NavigationItemResponse[]>("/api/v1/me/navigation", token))!;

        // Tenant A: cases, directory and documents in the plan for Administrator and Employee only, scheduling, engagement and reporting for all, marketing for staff; identity is Core.
        items.Select(item => item.Key).ShouldBe(keys);
    }

    [Fact]
    public async Task Me_WithoutAToken_Is401()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Add("X-Tenant", ApiDatabase.TenantA);

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PermissionGuardedEndpoint_Is403WithoutThePermission_And404WhenTheModuleIsHidden()
    {
        var (_, admin) = await factory.AddUserAsync([TenantRole.Administrator], Password);
        var (_, employee) = await factory.AddUserAsync([TenantRole.Employee], Password);
        var (_, client) = await factory.AddUserAsync([TenantRole.Client], Password);

        using var allowed = await GetAsync("/api/v1/test-perm/services", await factory.SignInAsync(admin, Password, Ct));
        using var denied = await GetAsync("/api/v1/test-perm/services", await factory.SignInAsync(employee, Password, Ct));
        using var hidden = await GetAsync("/api/v1/test-perm/services", await factory.SignInAsync(client, Password, Ct));

        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await denied.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString().ShouldBe("AUX-12028");
        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<T?> GetJsonAsync<T>(string path, string token)
    {
        using var response = await GetAsync(path, token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<T>(Ct);
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string token)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, Ct);
    }

    public sealed class Factory : AuthEndpointsTests.Factory
    {
        protected override IReadOnlyList<IModuleEndpoints> ModuleEndpoints { get; } = [new ServicesEndpoints()];
    }

    private sealed class ServicesEndpoints : IModuleEndpoints
    {
        public string ModuleCode => CasesModule.ModuleCode;

        public void Map(RouteGroupBuilder module) =>
            module.MapGet("/test-perm/services", () => TypedResults.Ok("services"))
                .RequirePermission(CasesPermissions.ManageServices)
                .ExcludeFromDescription();
    }
}
