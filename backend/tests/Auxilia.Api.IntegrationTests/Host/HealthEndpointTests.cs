using System.Net;

using Auxilia.ServiceDefaults;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Auxilia.Api.IntegrationTests.Host;

public sealed class HealthEndpointTests : IClassFixture<HealthEndpointTests.ApiFactory>
{
    private readonly ApiFactory factory;

    public HealthEndpointTests(ApiFactory factory) => this.factory = factory;

    [Theory]
    [InlineData(ServiceDefaultsExtensions.LivePath)]
    [InlineData(ServiceDefaultsExtensions.ReadyPath)]
    public async Task HealthEndpoint_ReturnsHealthy(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("Healthy");
    }

    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting("AuxiliaLogging:Storage", "none");
    }
}
