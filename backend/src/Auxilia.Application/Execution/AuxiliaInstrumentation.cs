using System.Diagnostics;
using System.Diagnostics.Metrics;

using Auxilia.Diagnostics;

namespace Auxilia.Application.Execution;

/// <summary>The Auxilia <see cref="ActivitySource"/> and operation metrics, exported by ServiceDefaults.</summary>
internal static class AuxiliaInstrumentation
{
    public const string DurationMetric = "auxilia.operation.duration";

    public const string CountMetric = "auxilia.operation.count";

    public static readonly ActivitySource ActivitySource = new(Telemetry.ActivitySourceName);

    public static readonly Meter Meter = new(Telemetry.MeterName);

    public static readonly Histogram<double> Duration =
        Meter.CreateHistogram<double>(DurationMetric, unit: "s", description: "Duration of Auxilia operations");

    public static readonly Counter<long> Count =
        Meter.CreateCounter<long>(CountMetric, description: "Auxilia operations by outcome");
}

/// <summary>Outcome tag of the operation metrics and traces.</summary>
internal static class OperationOutcome
{
    public const string Success = "success";

    /// <summary>Expected failure returned as <c>Result.Failure</c> (validation, rules, permissions) or a known exception.</summary>
    public const string Failure = "failure";

    /// <summary>Unexpected exception.</summary>
    public const string Error = "error";

    public const string Cancelled = "cancelled";
}
