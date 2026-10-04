using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Auxilia.Contracts.Common;
using Auxilia.Contracts.Identity;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Api.IntegrationTests.Identity;

/// <summary>
/// B-20 over HTTP (F17): Administrators list the open sessions of the tenant with the cards and revoke one; the revoked
/// access token stops working at once (deny-list); other roles get 403.
/// </summary>
public sealed class ActiveSessionEndpointsTests(AuthEndpointsTests.Factory factory) : IClassFixture<AuthEndpointsTests.Factory>
{
    private const string Password = "A long Passw0rd for tests!";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Administrator_SeesTheOpenSessions_AndRevokesOne()
    {
        var (_, adminName) = await factory.AddUserAsync([TenantRole.Administrator], Password);
        var admin = await factory.SignInAsync(adminName, Password, Ct);
        var (employeeId, employeeName) = await factory.AddUserAsync([TenantRole.Employee], Password);
        var employee = await factory.SignInAsync(employeeName, Password, Ct);

        var page = await GetAsync<PagedResponse<ActiveSessionResponse>>(admin, $"/api/v1/identity/sessions?filter[userName]={employeeName}");
        var session = page.Items.ShouldHaveSingleItem();
        (session.UserId, session.UserName, session.IsCurrent).ShouldBe((employeeId, employeeName, false));
        session.Roles.ShouldBe(["Employee"]);
        (await GetAsync<PagedResponse<ActiveSessionResponse>>(admin, $"/api/v1/identity/sessions?filter[userName]={adminName}"))
            .Items.ShouldHaveSingleItem().IsCurrent.ShouldBeTrue();

        var summary = await GetAsync<ActiveSessionSummaryResponse>(admin, "/api/v1/identity/sessions/summary");
        summary.Sessions.ShouldBeGreaterThanOrEqualTo(2);
        summary.ActiveNow.ShouldBeGreaterThanOrEqualTo(2);

        (await SendAsync(HttpMethod.Get, "/api/v1/identity/sessions", employee)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/identity/sessions/{session.Id}", employee)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await SendAsync(HttpMethod.Get, "/api/v1/me", employee)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/identity/sessions/{session.Id}", admin)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Get, "/api/v1/me", employee)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/identity/sessions/{session.Id}", admin)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync<PagedResponse<ActiveSessionResponse>>(admin, $"/api/v1/identity/sessions?filter[userName]={employeeName}")).Items.ShouldBeEmpty();
    }

    private async Task<T> GetAsync<T>(string token, string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path, token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token)
    {
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request, Ct);
    }
}
