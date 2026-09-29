using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Auxilia.Api.Endpoints;
using Auxilia.Api.Infrastructure;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Auxilia.Api.IntegrationTests.Host;

/// <summary>ProblemDetails with errorCode and traceId for every failure path (ADR 0012, skill auxilia-api-contract).</summary>
public sealed class ErrorResponseTests : IClassFixture<ErrorResponseTests.Factory>
{
    private const string SecretMessage = "secret internal detail";

    private readonly Factory factory;

    public ErrorResponseTests(Factory factory) => this.factory = factory;

    [Theory]
    [InlineData("not-found", HttpStatusCode.NotFound, "AUX-14003")]
    [InlineData("conflict", HttpStatusCode.Conflict, "AUX-10010")]
    [InlineData("precondition", HttpStatusCode.PreconditionFailed, "AUX-10011")]
    [InlineData("forbidden", HttpStatusCode.Forbidden, "AUX-11004")]
    [InlineData("throws", HttpStatusCode.InternalServerError, "AUX-10001")]
    [InlineData("timeout", HttpStatusCode.ServiceUnavailable, "AUX-10013")]
    public async Task FailingEndpoint_ReturnsProblemDetailsWithCode(string route, HttpStatusCode status, string errorCode)
    {
        var (response, problem) = await GetProblemAsync($"/api/v1/test/{route}");

        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        problem.GetProperty("errorCode").GetString().ShouldBe(errorCode);
        problem.GetProperty("type").GetString().ShouldBe(ProblemDetailsSetup.ErrorTypeBaseUri + errorCode);
        problem.GetProperty("status").GetInt32().ShouldBe((int)status);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.GetRawText().ShouldNotContain(SecretMessage);
    }

    [Fact]
    public async Task ValidationFailure_Returns400WithFieldErrors()
    {
        var (response, problem) = await GetProblemAsync("/api/v1/test/validation");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        problem.GetProperty("errorCode").GetString().ShouldBe("AUX-13001");
        problem.GetProperty("errors").GetProperty("fiscalCode")[0].GetString().ShouldBe("validation.fiscalCode.invalid");
    }

    [Fact]
    public async Task MalformedJson_Returns400RequestInvalid()
    {
        using var client = factory.CreateClient();
        using var body = new StringContent("{ not json", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(new Uri("/api/v1/test/echo", UriKind.Relative), body, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(response)).GetProperty("errorCode").GetString().ShouldBe("AUX-10019");
    }

    [Theory]
    [InlineData("/does-not-exist")]
    [InlineData("/api/v1/does-not-exist")]
    [InlineData("/api/v2/test/ok")]
    public async Task UnknownRouteOrVersion_Returns404EndpointNotFound(string path)
    {
        var (response, problem) = await GetProblemAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        problem.GetProperty("errorCode").GetString().ShouldBe("AUX-10017");
    }

    [Fact]
    public async Task VersionedEndpoint_ReportsSupportedVersions()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/test/ok", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("api-supported-versions").ShouldContain("1.0");
    }

    [Fact]
    public async Task AnyResponse_CarriesSecurityHeaders()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/test/ok", UriKind.Relative), TestContext.Current.CancellationToken);

        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
        response.Headers.GetValues("Content-Security-Policy").ShouldBe([SecurityHeaders.ApiContentSecurityPolicy]);
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
        response.Headers.Contains("Server").ShouldBeFalse();
    }

    [Fact]
    public async Task OpenApiDocument_PublishesVersionedRoutesWithoutVersionParameter()
    {
        using var client = factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);

        var paths = document.GetProperty("paths");
        paths.TryGetProperty("/api/v1/test/ok", out var ok).ShouldBeTrue(paths.GetRawText());
        ok.GetRawText().ShouldNotContain("\"version\"");
    }

    private async Task<(HttpResponseMessage Response, JsonElement Problem)> GetProblemAsync(string path)
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
        return (response, await ReadProblemAsync(response));
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

    public sealed class Factory : ApiFactory
    {
        protected override IApiEndpoints Endpoints { get; } = new TestEndpoints();
    }

    private sealed class TestEndpoints : IApiEndpoints
    {
        public void Map(RouteGroupBuilder api)
        {
            var test = api.MapGroup("/test");
            test.MapGet("/ok", () => TypedResults.Ok());
            test.MapGet("/not-found", () => Error.NotFound(14003, "Case not found").ToProblem());
            test.MapGet("/conflict", () => Errors.Host.ConcurrencyConflict().ToProblem());
            test.MapGet("/precondition", () => Errors.Host.PreconditionFailed().ToProblem());
            test.MapGet("/forbidden", () => Errors.Tenancy.CrossTenantAttempt().ToProblem());
            test.MapGet("/validation", () => Error.Validation(13001, "Invalid client", new Dictionary<string, string[]>
            {
                ["fiscalCode"] = ["validation.fiscalCode.invalid"],
            }).ToProblem());
            test.MapGet("/throws", IResult () => throw new InvalidOperationException(SecretMessage));
            test.MapGet("/timeout", IResult () => throw new InvalidOperationException(SecretMessage, new TimeoutException()));
            test.MapPost("/echo", (EchoRequest request) => TypedResults.Ok(request));
        }
    }

    private sealed record EchoRequest(string Name);
}
