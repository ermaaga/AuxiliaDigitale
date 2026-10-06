using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.Infrastructure;
using Auxilia.Api.IntegrationTests.Identity;
using Auxilia.Contracts.Cases;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Api.IntegrationTests.Host;

/// <summary>
/// F29 optimistic concurrency over HTTP: the detail answers with an ETag, a write needs <c>If-Match</c> (428 without,
/// 412 when somebody changed the resource in between), and every versioned write has a versioned GET on its route.
/// </summary>
public sealed class ResourceVersioningTests(ResourceVersioningTests.Factory factory) : IClassFixture<ResourceVersioningTests.Factory>
{
    private const string Password = "a long enough password";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void EveryVersionedWrite_HasAVersionedRead_OnTheSameRoute()
    {
        using var _ = factory.CreateClient();
        var endpoints = factory.Services.GetServices<EndpointDataSource>().SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToList();
        var writes = endpoints.Where(endpoint => endpoint.Metadata.GetMetadata<IfMatchMetadata>() is not null).ToList();

        writes.Count.ShouldBeGreaterThanOrEqualTo(27);
        var unversioned = writes
            .Where(write => !endpoints.Any(read =>
                read.RoutePattern.RawText == write.RoutePattern.RawText
                && read.Metadata.GetMetadata<ETagMetadata>() is not null
                && read.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Contains(HttpMethods.Get)))
            .Select(write => write.DisplayName)
            .ToList();
        unversioned.ShouldBeEmpty();
    }

    [Fact]
    public async Task Writes_NeedTheVersionTheCallerRead()
    {
        var admin = await AdministratorAsync();
        using var created = await SendAsync(HttpMethod.Post, "/api/v1/services", admin,
            new CreateServiceRequest("Versioned " + Guid.NewGuid().ToString("N")[..8], null, 10m, 30, null, null));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var service = (await created.Content.ReadFromJsonAsync<ServiceResponse>(Ct))!;
        var path = $"/api/v1/services/{service.Id}";
        UpdateServiceRequest Update(decimal price) => new(service.Name, null, price, 30, null, null, true);

        using var read = await SendAsync(HttpMethod.Get, path, admin);
        var etag = read.Headers.ETag!.ToString();
        etag.ShouldStartWith("W/\"");
        using (var again = await SendAsync(HttpMethod.Get, path, admin))
        {
            again.Headers.ETag!.ToString().ShouldBe(etag);
        }

        using var missing = await SendAsync(HttpMethod.Put, path, admin, Update(11m));
        await ShouldHaveCodeAsync(missing, HttpStatusCode.PreconditionRequired, "AUX-10025");

        // Somebody else saves first: the version the caller read is gone.
        using (var first = await SendAsync(HttpMethod.Put, path, admin, Update(12m), etag))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync(Ct));
        }

        using var stale = await SendAsync(HttpMethod.Put, path, admin, Update(13m), etag);
        await ShouldHaveCodeAsync(stale, HttpStatusCode.PreconditionFailed, "AUX-10011");
        using var staleDelete = await SendAsync(HttpMethod.Delete, path, admin, ifMatch: etag);
        await ShouldHaveCodeAsync(staleDelete, HttpStatusCode.PreconditionFailed, "AUX-10011");

        using var fresh = await SendAsync(HttpMethod.Get, path, admin);
        var current = fresh.Headers.ETag!.ToString();
        current.ShouldNotBe(etag);
        (await fresh.Content.ReadFromJsonAsync<ServiceResponse>(Ct))!.Price.ShouldBe(12m);
        using var deleted = await SendAsync(HttpMethod.Delete, path, admin, ifMatch: current);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // A resource the caller cannot read is the handler's business: 404, not a precondition error.
        using var gone = await SendAsync(HttpMethod.Put, path, admin, Update(14m));
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task IfMatch_StarMatchesAnyVersion()
    {
        var admin = await AdministratorAsync();
        using var created = await SendAsync(HttpMethod.Post, "/api/v1/services", admin,
            new CreateServiceRequest("Star " + Guid.NewGuid().ToString("N")[..8], null, 5m, 30, null, null));
        var service = (await created.Content.ReadFromJsonAsync<ServiceResponse>(Ct))!;

        using var deleted = await SendAsync(HttpMethod.Delete, $"/api/v1/services/{service.Id}", admin, ifMatch: "*");

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("W/\"abc\"", "W/\"abc\"", true)]
    [InlineData("\"abc\"", "W/\"abc\"", true)]
    [InlineData("W/\"x\", W/\"abc\"", "W/\"abc\"", true)]
    [InlineData("W/\"abd\"", "W/\"abc\"", false)]
    [InlineData("garbage", "W/\"abc\"", false)]
    public void Matches_UsesWeakComparison(string ifMatch, string current, bool expected) =>
        ResourceVersioning.Matches(ifMatch, current).ShouldBe(expected);

    private async Task<string> AdministratorAsync()
    {
        var (_, userName) = await factory.AddUserAsync([TenantRole.Administrator], Password);
        return await factory.SignInAsync(userName, Password, Ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? body = null, string? ifMatch = null)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        return await client.SendAsync(request, Ct);
    }

    private static async Task ShouldHaveCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync(Ct));
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString().ShouldBe(code);
    }

    public sealed class Factory : AuthEndpointsTests.Factory
    {
        protected override bool RequireIfMatch => true;
    }
}
