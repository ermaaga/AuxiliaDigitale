namespace Auxilia.Diagnostics;

/// <summary>Names shared by the code that emits telemetry and the OpenTelemetry setup in ServiceDefaults.</summary>
public static class Telemetry
{
    /// <summary>The <c>ActivitySource</c> of Auxilia operations (<c>IOperationRunner</c>, handlers, jobs).</summary>
    public const string ActivitySourceName = "Auxilia";

    /// <summary>The <c>Meter</c> of Auxilia metrics (<c>auxilia.operation.duration</c>, <c>auxilia.operation.count</c>).</summary>
    public const string MeterName = "Auxilia";
}
