using Auxilia.Api.Endpoints;
using Auxilia.Api.Endpoints.Cases;
using Auxilia.Api.Endpoints.Configuration;
using Auxilia.Api.Endpoints.Directory;
using Auxilia.Api.Endpoints.Documents;
using Auxilia.Api.Endpoints.Engagement;
using Auxilia.Api.Endpoints.Identity;
using Auxilia.Api.Endpoints.Localization;
using Auxilia.Api.Endpoints.Messaging;
using Auxilia.Api.Endpoints.Platform;
using Auxilia.Api.Endpoints.Scheduling;
using Auxilia.Api.Infrastructure;
using Auxilia.Api.Modules;
using Auxilia.Api.RateLimiting;
using Auxilia.Api.Tenancy;
using Auxilia.Application;
using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Infrastructure;
using Auxilia.Infrastructure.Adapters.Captcha.Altcha;
using Auxilia.Infrastructure.Adapters.Storage.Local;
using Auxilia.Infrastructure.Caching;
using Auxilia.Infrastructure.Logging;
using Auxilia.Infrastructure.Messaging;
using Auxilia.Infrastructure.Realtime;
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
builder.Services.AddTenantLogLevelSync();

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

builder.Services.AddAuxiliaForwardedHeaders(builder.Configuration);
builder.Services.AddAuxiliaAuthentication(builder.Configuration);
builder.Services.AddAuxiliaRateLimiting(builder.Configuration);
builder.Services.AddRealtime(builder.Configuration.GetConnectionString("Redis"));
builder.Services.AddSingleton<IApiEndpoints, AuthEndpoints>();
builder.Services.AddSingleton<IApiEndpoints, MeEndpoints>();
builder.Services.AddSingleton<IApiEndpoints, UserImageEndpoints>();
builder.Services.AddSingleton<IApiEndpoints, PlatformEndpoints>();
builder.Services.AddSingleton<IApiEndpoints, TenantLogEndpoints>();
builder.Services.AddSingleton<IApiEndpoints, LocalizationEndpoints>();
builder.Services.AddSingleton<IApiEndpoints, AdministratorEndpoints>();
builder.Services.AddSingleton<IApiEndpoints, ConfigurationEndpoints>();
builder.Services.AddSingleton<IApiEndpoints, CustomizationEndpoints>();
builder.Services.AddSingleton<IApiEndpoints, MessagingEndpoints>();
builder.Services.AddSingleton<IApiEndpoints, RolePermissionEndpoints>();
builder.Services.AddSingleton<IApiEndpoints, SpecializationEndpoints>();
builder.Services.AddSingleton<IApiEndpoints, RegistrationSubmissionEndpoints>();
builder.Services.AddSingleton<IModuleEndpoints, IdentityModuleEndpoints>();
builder.Services.AddSingleton<IModuleEndpoints, ClientEndpoints>();
builder.Services.AddSingleton<IModuleEndpoints, EmployeeEndpoints>();
builder.Services.AddSingleton<IModuleEndpoints, RegistrationEndpoints>();
builder.Services.AddSingleton<IModuleEndpoints, ServiceEndpoints>();
builder.Services.AddSingleton<IModuleEndpoints, CaseEndpoints>();
builder.Services.AddSingleton<IModuleEndpoints, DocumentEndpoints>();
builder.Services.AddSingleton<IModuleEndpoints, AppointmentEndpoints>();
builder.Services.AddSingleton<IModuleEndpoints, RequestEndpoints>();
builder.Services.AddSingleton<IModuleEndpoints, NotificationEndpoints>();

builder.Services.Configure<TenancyOptions>(builder.Configuration.GetSection(TenancyOptions.SectionName));
builder.Services.Configure<AltchaOptions>(builder.Configuration.GetSection(AltchaOptions.SectionName));
builder.Services.Configure<LocalStorageOptions>(builder.Configuration.GetSection(LocalStorageOptions.SectionName));

// Module descriptors are registered by AddApplication; their endpoints are IModuleEndpoints (MapModules below).

var app = builder.Build();

// First: every later component (rate limits, login audit, logs) sees the caller behind the BFF / reverse proxy.
app.UseForwardedHeaders();
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

// Server-to-client events (F16/F17); a connection closes when its access token expires, so a revoked session cannot
// keep listening.
app.MapHub<NotificationsHub>(RealtimeRegistration.HubPath, options => options.CloseOnAuthenticationExpiration = true);

var api = app.MapApiV1();
foreach (var endpoints in app.Services.GetServices<IApiEndpoints>())
{
    endpoints.Map(api);
}

api.MapModules(app.Services);

await app.RunAsync();
