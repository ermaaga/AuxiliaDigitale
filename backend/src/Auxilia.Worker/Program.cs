using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// The Worker only consumes queues (decision D-15, ADR 0007): no timers or scheduled services.
// Rebus consumers are registered in task P1-12.

using var host = builder.Build();

await host.RunAsync();
