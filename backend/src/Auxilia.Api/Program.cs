using Auxilia.Api.Endpoints;
using Auxilia.Api.Endpoints.Identity;
using Auxilia.Api.Infrastructure;
using Auxilia.Api.Modules;
using Auxilia.Api.RateLimiting;
using Auxilia.Api.Tenancy;
using Auxilia.Application;
using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Infrastructure;
using Auxilia.Infrastructure.Caching;
using Auxilia.Infrastructure.Messaging;
using Auxilia.Persistence.Catalog;
using Auxilia.Persistence.Tenant;
using Auxilia.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddAuxiliaProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddAuxiliaApiVersioning();
builder.Services.AddAuxiliaOpenApi();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure();

// The Catalog is the only connection string in configuration (user-secrets / environment, never committed).
var catalogConnectionString = builder.Configuration.GetConnectionString("Catalog")
    ?? throw new InvalidOperationException("ConnectionStrings:Catalog is not configured.");
builder.Services.AddCatalogPersistence(catalogConnectionString);
builder.Services.AddTenantPersistence();

// Redis/Valkey is the L2 of the reference-data cache; without it each node caches in memory only.
if (builder.Configuration.GetConnectionString("Redis") is { Length: > 0 } redis)
{
    builder.Services.AddRedisCache(redis);
}

// RabbitMQ (send only): messages leave through the outbox; without it they stay pending in ops.outbox_messages.
if (builder.Configuration.GetConnectionString("RabbitMq") is { Length: > 0 } rabbitMq)
{
    builder.Services.AddMessageBusClient(rabbitMq);
}

builder.Services.AddAuxiliaAuthentication(builder.Configuration);
builder.Services.AddAuxiliaRateLimiting(builder.Configuration);
builder.Services.AddSingleton<IApiEndpoints, AuthEndpoints>();
builder.Services.AddSingleton<IApiEndpoints, MeEndpoints>();

builder.Services.Configure<TenancyOptions>(builder.Configuration.GetSection(TenancyOptions.SectionName));

// Module descriptors are registered by AddApplication; their endpoints are IModuleEndpoints (MapModules below).

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseAuxiliaSecurityHeaders();

// Authentication goes before tenant resolution, so the token claim is authoritative.
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();

// After authentication and tenant resolution: partitions are per tenant and user.
app.UseRateLimiter();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapAuxiliaOpenApi();
app.MapJwks();

var api = app.MapApiV1();
foreach (var endpoints in app.Services.GetServices<IApiEndpoints>())
{
    endpoints.Map(api);
}

api.MapModules(app.Services);

await app.RunAsync();
