using System.Collections.Immutable;

using Serilog.Core;
using Serilog.Events;

namespace Auxilia.ServiceDefaults.Logging;

/// <summary>
/// Minimum log level per tenant with an expiry (decision D-28). The default level applies to every event; a tenant
/// override (e.g. Debug until 18:00) applies to that tenant's events and ends by itself at its expiry, without jobs
/// or restarts. <see cref="MinimumLevel"/> follows the lowest active level so lower events are produced only when
/// some override needs them. Overrides are set from the tenant settings (tasks P1-10, S-07).
/// </summary>
public sealed class TenantLogLevels
{
    private readonly TimeProvider timeProvider;
    private readonly Lock gate = new();
    private ImmutableDictionary<string, TenantLevel> overrides = ImmutableDictionary.Create<string, TenantLevel>(StringComparer.Ordinal);
    private long nextExpiryUtcTicks = long.MaxValue;

    public TenantLogLevels(LogEventLevel defaultLevel, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        DefaultLevel = defaultLevel;
        this.timeProvider = timeProvider;
        MinimumLevel = new LoggingLevelSwitch(defaultLevel);
    }

    public LogEventLevel DefaultLevel { get; }

    /// <summary>Controls the logger minimum level: the lowest of the default and the active overrides.</summary>
    public LoggingLevelSwitch MinimumLevel { get; }

    public void SetOverride(string tenantSlug, LogEventLevel level, DateTimeOffset until)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);

        lock (gate)
        {
            overrides = until > timeProvider.GetUtcNow()
                ? overrides.SetItem(tenantSlug, new TenantLevel(level, until))
                : overrides.Remove(tenantSlug);
            Recalculate();
        }
    }

    public void ClearOverride(string tenantSlug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);

        lock (gate)
        {
            overrides = overrides.Remove(tenantSlug);
            Recalculate();
        }
    }

    /// <summary>The effective minimum level for an event of <paramref name="tenantSlug"/> (null = no tenant).</summary>
    public LogEventLevel LevelFor(string? tenantSlug)
    {
        var now = timeProvider.GetUtcNow();
        if (now.UtcTicks >= Volatile.Read(ref nextExpiryUtcTicks))
        {
            lock (gate)
            {
                Recalculate();
            }
        }

        return tenantSlug is not null && overrides.TryGetValue(tenantSlug, out var tenantLevel) && tenantLevel.Until > now
            ? tenantLevel.Level
            : DefaultLevel;
    }

    private void Recalculate()
    {
        var now = timeProvider.GetUtcNow();
        overrides = overrides.RemoveRange(overrides.Where(item => item.Value.Until <= now).Select(item => item.Key));
        Volatile.Write(ref nextExpiryUtcTicks, overrides.IsEmpty ? long.MaxValue : overrides.Values.Min(item => item.Until.UtcTicks));
        MinimumLevel.MinimumLevel = overrides.Values.Select(item => item.Level).Append(DefaultLevel).Min();
    }

    private sealed record TenantLevel(LogEventLevel Level, DateTimeOffset Until);
}
