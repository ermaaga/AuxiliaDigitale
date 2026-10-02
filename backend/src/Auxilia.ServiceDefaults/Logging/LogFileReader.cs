using System.Runtime.CompilerServices;
using System.Text;

using Auxilia.Diagnostics.Logging;
using Auxilia.ServiceDefaults.Logging.Storage;

namespace Auxilia.ServiceDefaults.Logging;

/// <summary>Reads the daily files of the configured storage for the Log page (D-17); with no storage there is nothing to read.</summary>
public sealed class LogFileReader : ILogFileReader
{
    private readonly ILogFileStore? store;

    public LogFileReader(ILogFileStore? store)
    {
        this.store = store;
    }

    public async IAsyncEnumerable<string> ReadTenantDayAsync(
        string tenantSlug, DateOnly day, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // An invalid slug would read the platform file: never.
        if (store is null || !LogFilePaths.IsValidSlug(tenantSlug))
        {
            yield break;
        }

        var path = LogFilePaths.For(tenantSlug, new DateTimeOffset(day, TimeOnly.MinValue, TimeSpan.Zero));
        await using var stream = await store.OpenReadAsync(path, cancellationToken);
        if (stream is null)
        {
            yield break;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            // A line still being appended by another process is incomplete: the reader skips what it cannot parse.
            if (line.Length > 0)
            {
                yield return line;
            }
        }
    }
}
