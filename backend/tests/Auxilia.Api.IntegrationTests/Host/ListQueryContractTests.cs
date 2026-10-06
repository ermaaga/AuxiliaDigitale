using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Identity;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Api.IntegrationTests.Host;

/// <summary>
/// F28 list contract: declared <c>filter[…]</c> keys filter, unknown ones are refused with 400 (never silently ignored),
/// unknown sort fields too; the export endpoint forwards a list's query and is not affected.
/// </summary>
public sealed class ListQueryContractTests(AuthEndpointsTests.Factory factory) : IClassFixture<AuthEndpointsTests.Factory>
{
    private const string Password = "a long enough password";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/v1/clients?filter[lastName]=Rossi")]
    [InlineData("/api/v1/clients?FILTER[LASTNAME]=Rossi")]
    [InlineData("/api/v1/cases?filter[status]=Inserted")]
    [InlineData("/api/v1/identity/login-attempts?filter[succeeded]=false")]
    [InlineData("/api/v1/exports/clients?format=csv&columns=lastName&filter[whatever]=x")]
    public async Task DeclaredFilters_AndExports_AreAccepted(string path)
    {
        using var response = await GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData("/api/v1/clients?filter[nickname]=x", "filter[nickname]")]
    [InlineData("/api/v1/cases?filter[status]=Inserted&filter[colour]=red", "filter[colour]")]
    [InlineData("/api/v1/identity/login-attempts?filter[ip]=10.0.0.1", "filter[ip]")]
    [InlineData("/api/v1/employees?filter[x]=1", "filter[x]")]
    public async Task UnknownFilters_AreRefused(string path, string key)
    {
        using var response = await GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("errorCode").GetString().ShouldBe("AUX-10020");
        problem.GetProperty("errors").GetProperty(key)[0].GetString().ShouldBe("validation.paging.filter");
    }

    [Theory]
    [InlineData("/api/v1/clients?sort=-nickname")]
    [InlineData("/api/v1/cases?sort=colour")]
    public async Task UnknownSortFields_AreRefused(string path)
    {
        using var response = await GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").TryGetProperty("sort", out _).ShouldBeTrue();
    }

    private async Task<HttpResponseMessage> GetAsync(string path)
    {
        var (_, userName) = await factory.AddUserAsync([TenantRole.Administrator], Password);
        var token = await factory.SignInAsync(userName, Password, Ct);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, Ct);
    }
}
