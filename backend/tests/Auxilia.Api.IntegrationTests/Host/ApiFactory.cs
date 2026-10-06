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

    /// <summary>The <c>sub</c> claim of the test caller (default: a new id per request).</summary>
    public const string TestUserHeader = "X-Test-User";

    public const string BaseDomain = "auxilia.test";

    protected virtual IApiEndpoints? Endpoints => null;

    /// <summary>Whether writes of versioned resources need <c>If-Match</c> (production: always).</summary>
    protected virtual bool RequireIfMatch => false;

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

        // Suites share one client IP (none, in the test server): rate limits are tested on their own (RateLimitingTests).
        builder.UseSetting("RateLimiting:Enabled", "false");

        // Suites written before F29 send no If-Match; ResourceVersioningTests turn the requirement back on.
        builder.UseSetting("Concurrency:RequireIfMatch", RequireIfMatch ? "true" : "false");

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
                        [new Claim("sub", context.Request.Headers[TestUserHeader].FirstOrDefault() ?? Guid.CreateVersion7().ToString()), new Claim("tenant", tenant.ToString()), .. roles], "Test"));
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
