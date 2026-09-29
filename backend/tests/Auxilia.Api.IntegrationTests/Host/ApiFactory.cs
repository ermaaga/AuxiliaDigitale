using Auxilia.Api.Endpoints;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Api.IntegrationTests.Host;

/// <summary>The API in Development, logging to the console only; <see cref="Endpoints"/> are mapped on <c>/api/v1</c>.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    protected virtual IApiEndpoints? Endpoints => null;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("AuxiliaLogging:Storage", "none");

        if (Endpoints is { } endpoints)
        {
            builder.ConfigureServices(services => services.AddSingleton(endpoints));
        }
    }
}
