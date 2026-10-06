using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Identity;
using Auxilia.Contracts.Configuration;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Api.IntegrationTests.Configuration;

/// <summary>F21 personal views over HTTP on PostgreSQL: create, list, switch the default, rename, delete; own grids only.</summary>
public sealed class GridViewEndpointsTests(AuthEndpointsTests.Factory factory) : IClassFixture<AuthEndpointsTests.Factory>
{
    private const string Password = "a long enough password";
    private const string Views = "/api/v1/me/grids/cases.cases/views";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Views_AreSaved_OneDefault_PerUser()
    {
        var token = await SignInAsync(TenantRole.Employee);
        var filters = new Dictionary<string, string> { ["status"] = "InProgress", ["clientName"] = "rossi" };

        using var first = await SendAsync(HttpMethod.Post, Views, token, new SaveGridViewRequest("Aperte", ["amountPaid"], filters, "-startedOn", true));
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync(Ct));
        var open = (await first.Content.ReadFromJsonAsync<GridViewResponse>(Ct))!;
        open.HiddenColumns.ShouldBe(["amountPaid"]);
        open.Filters.ShouldBe(filters, ignoreOrder: true);
        (open.Sort, open.IsDefault).ShouldBe(("-startedOn", true));

        using var duplicate = await SendAsync(HttpMethod.Post, Views, token, new SaveGridViewRequest("APERTE", null, null, null, false));
        duplicate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await duplicate.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").GetProperty("name")[0].GetString()
            .ShouldBe("validation.gridViews.nameTaken");

        // The second default takes over: two saves in one transaction keep the unique default index happy.
        using var second = await SendAsync(HttpMethod.Post, Views, token, new SaveGridViewRequest("Tutte", null, null, "client", true));
        var all = (await second.Content.ReadFromJsonAsync<GridViewResponse>(Ct))!;
        var listed = await ListAsync(token);
        listed.Select(view => (view.Name, view.IsDefault)).ShouldBe([("Aperte", false), ("Tutte", true)]);

        using var renamed = await SendAsync(HttpMethod.Put, $"{Views}/{open.Id}", token, new SaveGridViewRequest("In lavorazione", ["amountPaid"], filters, "-startedOn", true));
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync(Ct));
        (await ListAsync(token)).Select(view => (view.Name, view.IsDefault)).ShouldBe([("In lavorazione", true), ("Tutte", false)]);

        // Another user sees nothing of these and cannot touch them.
        var other = await SignInAsync(TenantRole.Employee);
        (await ListAsync(other)).ShouldBeEmpty();
        using var foreign = await SendAsync(HttpMethod.Delete, $"{Views}/{all.Id}", other);
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var deleted = await SendAsync(HttpMethod.Delete, $"{Views}/{all.Id}", token);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(token)).Select(view => view.Name).ShouldBe(["In lavorazione"]);
    }

    [Fact]
    public async Task GridsTheRolesDoNotSee_AreNotFound()
    {
        var token = await SignInAsync(TenantRole.Client);

        using var hidden = await SendAsync(HttpMethod.Get, "/api/v1/me/grids/directory.clients/views", token);
        using var unknown = await SendAsync(HttpMethod.Post, "/api/v1/me/grids/no.such/views", token, new SaveGridViewRequest("X", null, null, null, false));

        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<string> SignInAsync(TenantRole role)
    {
        var (_, userName) = await factory.AddUserAsync([role], Password);
        return await factory.SignInAsync(userName, Password, Ct);
    }

    private async Task<GridViewResponse[]> ListAsync(string token)
    {
        using var response = await SendAsync(HttpMethod.Get, Views, token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<GridViewResponse[]>(Ct))!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? body = null)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        return await client.SendAsync(request, Ct);
    }
}
