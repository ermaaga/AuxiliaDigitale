using Serilog.Core;
using Serilog.Events;

namespace Auxilia.ServiceDefaults.Logging;

/// <summary>Drops events below the effective level of their tenant (<see cref="TenantLogLevels"/>, D-28).</summary>
public sealed class TenantLevelFilter : ILogEventFilter
{
    private readonly TenantLogLevels levels;

    public TenantLevelFilter(TenantLogLevels levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        this.levels = levels;
    }

    public bool IsEnabled(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var tenant = logEvent.Properties.TryGetValue(LogProperties.TenantSlug, out var value) && value is ScalarValue { Value: string slug }
            ? slug
            : null;

        return logEvent.Level >= levels.LevelFor(tenant);
    }
}
