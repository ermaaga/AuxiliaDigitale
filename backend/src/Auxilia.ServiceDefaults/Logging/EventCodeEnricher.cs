using Auxilia.Diagnostics;

using Serilog.Core;
using Serilog.Events;

namespace Auxilia.ServiceDefaults.Logging;

/// <summary>Adds <c>EventCode = "AUX-" + EventId</c> to events whose <c>EventId</c> is an Auxilia code (ADR 0006).</summary>
public sealed class EventCodeEnricher : ILogEventEnricher
{
    private static readonly int FirstCode = EventRegistry.Ranges().Min(range => range.Start);
    private static readonly int LastCode = EventRegistry.Ranges().Max(range => range.End);

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        if (logEvent.Properties.TryGetValue("EventId", out var eventId)
            && eventId is StructureValue structure
            && structure.Properties.FirstOrDefault(property => property.Name == "Id")?.Value is ScalarValue { Value: int id }
            && id >= FirstCode && id <= LastCode)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(LogProperties.EventCode, $"AUX-{id}"));
        }
    }
}
