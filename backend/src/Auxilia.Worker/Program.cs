using Auxilia.ServiceDefaults;
using Auxilia.Worker;

using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddWorkerServices(builder.Configuration);

// The Worker only consumes queues (decision D-15, ADR 0007): no timers or scheduled services.
using var host = builder.Build();

await host.RunAsync();
