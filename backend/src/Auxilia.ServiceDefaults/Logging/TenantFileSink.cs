using System.Text;

using Auxilia.Diagnostics;
using Auxilia.ServiceDefaults.Logging.Storage;

using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Parsing;

namespace Auxilia.ServiceDefaults.Logging;

/// <summary>
/// Batched sink writing each event as a JSON line to its daily file per tenant (D-17). If the storage fails, the batch
/// goes to the local buffer and <c>AUX-10015</c> is written once to the diagnostics output (console); <c>AUX-10016</c>
/// when the storage works again. If the buffer fails too the exception reaches Serilog, which retries the batch.
/// </summary>
public sealed class TenantFileSink : IBatchedLogEventSink
{
    private static readonly MessageTemplateParser TemplateParser = new();

    private readonly ILogFileStore store;
    private readonly ILogFileStore buffer;
    private readonly ITextFormatter formatter;
    private readonly TextWriter diagnostics;
    private readonly TimeProvider timeProvider;
    private int storageUnavailable;

    public TenantFileSink(ILogFileStore store, ILogFileStore buffer, ITextFormatter formatter, TextWriter diagnostics, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(formatter);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.store = store;
        this.buffer = buffer;
        this.formatter = formatter;
        this.diagnostics = diagnostics;
        this.timeProvider = timeProvider;
    }

    public async Task EmitBatchAsync(IReadOnlyCollection<LogEvent> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        foreach (var file in batch.GroupBy(LogFilePaths.For))
        {
            var content = Render(file);

            try
            {
                await store.AppendAsync(file.Key, content, CancellationToken.None);
            }
#pragma warning disable CA1031 // Any storage failure is converted to a coded event and a local buffer (ADR 0012: catch to add meaning).
            catch (Exception exception)
#pragma warning restore CA1031
            {
                if (Interlocked.Exchange(ref storageUnavailable, 1) == 0)
                {
                    Report(EventCodes.Host.LogStorageUnavailable, "Host.LogStorageUnavailable", LogEventLevel.Error,
                        "Log storage unavailable: events are written to the console and the local buffer", exception);
                }

                await buffer.AppendAsync(file.Key, content, CancellationToken.None);
                continue;
            }

            if (Interlocked.Exchange(ref storageUnavailable, 0) == 1)
            {
                Report(EventCodes.Host.LogStorageRecovered, "Host.LogStorageRecovered", LogEventLevel.Information,
                    "Log storage available again", exception: null);
            }
        }
    }

    public Task OnEmptyBatchAsync() => Task.CompletedTask;

    private ReadOnlyMemory<byte> Render(IEnumerable<LogEvent> events)
    {
        using var writer = new StringWriter();
        foreach (var logEvent in events)
        {
            formatter.Format(logEvent, writer);
        }

        return Encoding.UTF8.GetBytes(writer.ToString());
    }

    private void Report(int code, string name, LogEventLevel level, string message, Exception? exception)
    {
        var logEvent = new LogEvent(
            timeProvider.GetUtcNow(),
            level,
            exception,
            TemplateParser.Parse(message),
            [
                new LogEventProperty("EventId", new StructureValue(
                [
                    new LogEventProperty("Id", new ScalarValue(code)),
                    new LogEventProperty("Name", new ScalarValue(name)),
                ])),
                new LogEventProperty(LogProperties.EventCode, new ScalarValue($"AUX-{code}")),
            ]);

        lock (diagnostics)
        {
            formatter.Format(logEvent, diagnostics);
            diagnostics.Flush();
        }
    }
}
