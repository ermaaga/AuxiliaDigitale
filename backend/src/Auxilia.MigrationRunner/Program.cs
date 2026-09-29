// auxctl — migrations, tenant provisioning, legacy import, manual job runs (tasks P1-09, E-01…E-06).
// Only `diagnostics registry` exists so far (P1-02); P1-09 introduces the full command set.

using Auxilia.Diagnostics;

if (args is ["diagnostics", "registry", .. var options])
{
    var markdown = EventRegistry.RenderMarkdown();

    if (options is [])
    {
        await Console.Out.WriteAsync(markdown);
        return 0;
    }

    if (options is ["--output", var output])
    {
        await File.WriteAllTextAsync(output, markdown);
        await Console.Out.WriteLineAsync($"auxctl: registry written to {output}");
        return 0;
    }
}

await Console.Error.WriteLineAsync("usage: auxctl diagnostics registry [--output <file>]");

return 1;
