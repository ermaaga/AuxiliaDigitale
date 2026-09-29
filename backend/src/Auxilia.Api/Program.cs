using Auxilia.Api.Endpoints;
using Auxilia.Api.Infrastructure;
using Auxilia.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddAuxiliaProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddAuxiliaApiVersioning();
builder.Services.AddAuxiliaOpenApi();

// Modules, persistence and security are registered from task P1-05 onwards.

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseAuxiliaSecurityHeaders();

app.MapDefaultEndpoints();
app.MapAuxiliaOpenApi();

var api = app.MapApiV1();
foreach (var endpoints in app.Services.GetServices<IApiEndpoints>())
{
    endpoints.Map(api);
}

await app.RunAsync();
