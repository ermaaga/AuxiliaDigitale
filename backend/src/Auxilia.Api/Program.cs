using Auxilia.Api.Endpoints;
using Auxilia.Api.Infrastructure;
using Auxilia.Api.Tenancy;
using Auxilia.Application;
using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Infrastructure;
using Auxilia.Infrastructure.Caching;
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

builder.Services.Configure<TenancyOptions>(builder.Configuration.GetSection(TenancyOptions.SectionName));

// Modules and security are registered from task P1-08 onwards.

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseAuxiliaSecurityHeaders();

// Authentication (P2) goes before tenant resolution, so the token claim is authoritative.
app.UseMiddleware<TenantResolutionMiddleware>();

app.MapDefaultEndpoints();
app.MapAuxiliaOpenApi();

var api = app.MapApiV1();
foreach (var endpoints in app.Services.GetServices<IApiEndpoints>())
{
    endpoints.Map(api);
}

await app.RunAsync();
