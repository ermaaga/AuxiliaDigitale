// auxctl — migrations, tenant lifecycle, manual job runs, diagnostics (task P1-09; legacy import E-01…E-06).

using Auxilia.MigrationRunner;
using Auxilia.MigrationRunner.Cli;

using Microsoft.Extensions.Hosting;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

// The host (and the Catalog connection) is built only by commands that need it: `diagnostics registry` works without.
var host = new Lazy<IHost>(() => AuxctlHost.Build());
try
{
    return await new AuxctlCli(() => host.Value.Services, Console.Out, Console.Error, Console.In).RunAsync(args, cancellation.Token);
}
finally
{
    if (host.IsValueCreated)
    {
        host.Value.Dispose();
    }
}
