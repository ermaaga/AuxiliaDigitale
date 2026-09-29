using System.Security.Claims;

using Auxilia.Api.Endpoints;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Api.IntegrationTests.Host;

/// <summary>
/// The API in Development on the shared <see cref="ApiDatabase"/>, logging to the console only; <see cref="Endpoints"/>
/// are mapped on <c>/api/v1</c>. Until authentication exists (P2), header <see cref="TestTenantClaimHeader"/> makes the
/// caller authenticated with that <c>tenant</c> claim.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string TestTenantClaimHeader = "X-Test-Tenant-Claim";

    public const string BaseDomain = "auxilia.test";

    protected virtual IApiEndpoints? Endpoints => null;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("AuxiliaLogging:Storage", "none");
        builder.UseSetting("ConnectionStrings:Catalog", ApiDatabase.Instance.CatalogConnectionString);
        builder.UseSetting("Tenancy:BaseDomains:0", BaseDomain);

        builder.ConfigureServices(services =>
        {
            services.AddTransient<IStartupFilter, TestAuthenticationStartupFilter>();
            if (Endpoints is { } endpoints)
            {
                services.AddSingleton(endpoints);
            }
        });
    }

    private sealed class TestAuthenticationStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(TestTenantClaimHeader, out var tenant))
                {
                    context.User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", Guid.CreateVersion7().ToString()), new Claim("tenant", tenant.ToString())], "Test"));
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
