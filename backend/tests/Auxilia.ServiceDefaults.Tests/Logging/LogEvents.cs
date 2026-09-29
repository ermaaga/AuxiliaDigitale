using Serilog.Events;
using Serilog.Parsing;

namespace Auxilia.ServiceDefaults.Tests.Logging;

internal static class LogEvents
{
    private static readonly MessageTemplateParser Parser = new();

    public static readonly DateTimeOffset Noon = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    public static LogEvent Create(
        LogEventLevel level = LogEventLevel.Information,
        string? tenant = null,
        DateTimeOffset? timestamp = null,
        params LogEventProperty[] properties)
    {
        var all = properties.ToList();
        if (tenant is not null)
        {
            all.Add(new LogEventProperty("TenantSlug", new ScalarValue(tenant)));
        }

        return new LogEvent(timestamp ?? Noon, level, null, Parser.Parse("Test event"), all);
    }

    public static LogEventProperty EventId(int id) =>
        new("EventId", new StructureValue([new LogEventProperty("Id", new ScalarValue(id))]));
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
