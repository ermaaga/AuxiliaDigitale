using Auxilia.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Modules, persistence, security and OpenAPI are registered from task P1-04 onwards.

var app = builder.Build();

app.MapDefaultEndpoints();

await app.RunAsync();
