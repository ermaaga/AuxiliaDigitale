using Auxilia.Application;
using Auxilia.Infrastructure;
using Auxilia.Infrastructure.Caching;
using Auxilia.Persistence.Catalog;
using Auxilia.Persistence.Tenant;
using Auxilia.ServiceDefaults;

using Microsoft.Extensions.Configuration;

using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddApplication();
builder.Services.AddInfrastructure();

// Same Catalog and tenant databases as the Api (user-secrets / environment, never committed).
var catalogConnectionString = builder.Configuration.GetConnectionString("Catalog")
    ?? throw new InvalidOperationException("ConnectionStrings:Catalog is not configured.");
builder.Services.AddCatalogPersistence(catalogConnectionString);
builder.Services.AddTenantPersistence();

if (builder.Configuration.GetConnectionString("Redis") is { Length: > 0 } redis)
{
    builder.Services.AddRedisCache(redis);
}

// The Worker only consumes queues (decision D-15, ADR 0007): no timers or scheduled services.
// Rebus consumers are registered in task P1-12.

using var host = builder.Build();

await host.RunAsync();
