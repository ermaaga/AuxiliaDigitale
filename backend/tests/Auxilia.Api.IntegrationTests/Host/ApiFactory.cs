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
/// are mapped on <c>/api/v1</c>. Besides real bearer tokens, header <see cref="TestTenantClaimHeader"/> makes the
/// caller authenticated with that <c>tenant</c> claim.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string TestTenantClaimHeader = "X-Test-Tenant-Claim";

    /// <summary>Comma-separated tenant roles added as <c>role</c> claims (with <see cref="TestTenantClaimHeader"/>).</summary>
    public const string TestRolesHeader = "X-Test-Roles";

    public const string BaseDomain = "auxilia.test";

    protected virtual IApiEndpoints? Endpoints => null;

    /// <summary>Module endpoints mapped through <c>MapModules</c> (tenant + module visibility filters).</summary>
    protected virtual IReadOnlyList<IModuleEndpoints> ModuleEndpoints => [];

    /// <summary>Redis of the reference-data cache; none by default (in-memory cache only).</summary>
    protected virtual string RedisConnectionString => string.Empty;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("AuxiliaLogging:Storage", "none");
        builder.UseSetting("ConnectionStrings:Catalog", ApiDatabase.Instance.CatalogConnectionString);
        builder.UseSetting("Tenancy:BaseDomains:0", BaseDomain);
        builder.UseSetting("ConnectionStrings:Redis", RedisConnectionString);

        builder.ConfigureServices(services =>
        {
            services.AddTransient<IStartupFilter, TestAuthenticationStartupFilter>();
            if (Endpoints is { } endpoints)
            {
                services.AddSingleton(endpoints);
            }

            foreach (var moduleEndpoints in ModuleEndpoints)
            {
                services.AddSingleton(moduleEndpoints);
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
                    var roles = context.Request.Headers[TestRolesHeader].ToString()
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(role => new Claim("role", role));
                    context.User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", Guid.CreateVersion7().ToString()), new Claim("tenant", tenant.ToString()), .. roles], "Test"));
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
