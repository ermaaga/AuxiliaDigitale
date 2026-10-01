using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Host;
using Auxilia.Api.IntegrationTests.Platform;
using Auxilia.Contracts.Configuration;

namespace Auxilia.Api.IntegrationTests.Configuration;

/// <summary>
/// F20/F21 over HTTP: custom fields and grid layouts edited by the System (tenant-scoped platform token, D-21), and read
/// by the signed-in tenant user through <c>/me</c>.
/// </summary>
public sealed class CustomizationEndpointsTests : IClassFixture<PlatformIdentityTests.Factory>
{
    private const string Grid = "identity.loginAttempts";

    private readonly PlatformIdentityTests.Factory factory;

    public CustomizationEndpointsTests(PlatformIdentityTests.Factory factory) => this.factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CustomFields_AreManagedByTheSystemAndReadByUsers()
    {
        var token = await PlatformTenantTokens.IssueAsync(factory, ApiDatabase.TenantA, Ct);
        var key = "F" + Guid.NewGuid().ToString("N")[..8];

        using var entities = await SendAsync(HttpMethod.Get, "/api/v1/custom-fields/entities", token);
        (await entities.Content.ReadFromJsonAsync<CustomFieldEntityResponse[]>(Ct))!.Select(entity => entity.Code)
            .ShouldBe(["appointment", "case", "client", "document", "request"]);

        using var invalid = await SendAsync(HttpMethod.Post, "/api/v1/custom-fields", token,
            new CreateCustomFieldRequest("client", key, "Area", "Select", [], false, null, null, true, false, 0));
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(invalid)).ShouldBe("AUX-20015");

        using var created = await SendAsync(HttpMethod.Post, "/api/v1/custom-fields", token,
            new CreateCustomFieldRequest("client", key, "CAF", "Boolean", null, false, "Area", "#72fa29", true, true, 1));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var id = (await created.Content.ReadFromJsonAsync<CreateCustomFieldResponse>(Ct))!.Id;

        using var duplicate = await SendAsync(HttpMethod.Post, "/api/v1/custom-fields", token,
            new CreateCustomFieldRequest("client", key.ToLowerInvariant(), "Dup", "Text", null, false, null, null, false, false, 0));
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await SendAsync(HttpMethod.Put, $"/api/v1/custom-fields/{id}", token,
            new UpdateCustomFieldRequest("CAF (servizi)", null, false, "Area", "#72fa29", true, true, 2))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var list = await SendAsync(HttpMethod.Get, "/api/v1/custom-fields?filter[entityType]=client", token);
        var field = (await list.Content.ReadFromJsonAsync<CustomFieldDefinitionResponse[]>(Ct))!.Single(item => item.Id == id);
        (field.Label, field.Type, field.BadgeColor, field.DashboardCounter, field.Order).ShouldBe(("CAF (servizi)", "Boolean", "#72fa29", true, 2));

        using var mine = await AsUserAsync(HttpMethod.Get, "/api/v1/me/custom-fields/client", "Employee");
        mine.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await mine.Content.ReadFromJsonAsync<CustomFieldDefinitionResponse[]>(Ct))!.ShouldContain(item => item.Id == id);
        (await AsUserAsync(HttpMethod.Get, "/api/v1/me/custom-fields/ghost", "Employee")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await SendAsync(HttpMethod.Delete, $"/api/v1/custom-fields/{id}", token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/custom-fields/{id}", token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GridLayouts_AreSetPerRoleAndReachTheUser()
    {
        var token = await PlatformTenantTokens.IssueAsync(factory, ApiDatabase.TenantA, Ct);

        using var grids = await SendAsync(HttpMethod.Get, "/api/v1/grids", token);
        var grid = (await grids.Content.ReadFromJsonAsync<GridResponse[]>(Ct))!.Single(item => item.Key == Grid);
        grid.Columns[0].ShouldBe(new GridColumnResponse("attemptedAt", "Date", true, false, false, true));
        grid.Layouts.ShouldHaveSingleItem().Role.ShouldBe("Administrator");

        using var set = await SendAsync(HttpMethod.Put, $"/api/v1/grids/{Grid}/layouts/Administrator", token,
            new SetGridLayoutRequest([new("userName", true), new("attemptedAt", true), new("ipAddress", false)]));
        set.StatusCode.ShouldBe(HttpStatusCode.OK, await set.Content.ReadAsStringAsync(Ct));
        var layout = (await set.Content.ReadFromJsonAsync<GridRoleLayoutResponse>(Ct))!;
        layout.IsCustomized.ShouldBeTrue();
        layout.Columns.Take(3).Select(column => (column.Key, column.Visible)).ShouldBe([("userName", true), ("attemptedAt", true), ("ipAddress", false)]);

        using var mine = await AsUserAsync(HttpMethod.Get, $"/api/v1/me/grids/{Grid}", "Employee,Administrator");
        var myLayout = (await mine.Content.ReadFromJsonAsync<MyGridLayoutResponse>(Ct))!;
        (myLayout.Role, myLayout.IsCustomized).ShouldBe(("Administrator", true));
        myLayout.Columns[0].Key.ShouldBe("userName");
        (await AsUserAsync(HttpMethod.Get, $"/api/v1/me/grids/{Grid}", "Client")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var hidden = await SendAsync(HttpMethod.Put, $"/api/v1/grids/{Grid}/layouts/Administrator", token,
            new SetGridLayoutRequest([new("attemptedAt", false)]));
        hidden.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(hidden)).ShouldBe("AUX-20022");
        (await SendAsync(HttpMethod.Put, $"/api/v1/grids/{Grid}/layouts/Client", token, new SetGridLayoutRequest([new("userName", true)])))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var reset = await SendAsync(HttpMethod.Delete, $"/api/v1/grids/{Grid}/layouts/Administrator", token);
        (await reset.Content.ReadFromJsonAsync<GridRoleLayoutResponse>(Ct))!.IsCustomized.ShouldBeFalse();
        using var again = await AsUserAsync(HttpMethod.Get, $"/api/v1/me/grids/{Grid}", "Administrator");
        (await again.Content.ReadFromJsonAsync<MyGridLayoutResponse>(Ct))!.Columns[0].Key.ShouldBe("attemptedAt");
    }

    [Fact]
    public async Task Editing_IsForTheSystemOnly_AndPlatformTokensDoNotReadAsUsers()
    {
        foreach (var path in new[] { "/api/v1/custom-fields", "/api/v1/grids" })
        {
            using var administrator = await AsUserAsync(HttpMethod.Get, path, "Administrator");
            administrator.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await ErrorCodeAsync(administrator)).ShouldBe("AUX-12040");
        }

        var token = await PlatformTenantTokens.IssueAsync(factory, ApiDatabase.TenantA, Ct);
        (await SendAsync(HttpMethod.Get, $"/api/v1/me/grids/{Grid}", token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Get, "/api/v1/me/custom-fields/client", token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> AsUserAsync(HttpMethod method, string path, string roles)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add(ApiFactory.TestTenantClaimHeader, ApiDatabase.TenantA);
        request.Headers.Add(ApiFactory.TestRolesHeader, roles);
        return await client.SendAsync(request, Ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? body = null)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body, body.GetType()) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, Ct);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString();
}
