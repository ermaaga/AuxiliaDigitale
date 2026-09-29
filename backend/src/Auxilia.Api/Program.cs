var builder = WebApplication.CreateBuilder(args);

// Service registration (modules, persistence, security, OpenAPI) is added from task P1-03 onwards.

var app = builder.Build();

await app.RunAsync();
