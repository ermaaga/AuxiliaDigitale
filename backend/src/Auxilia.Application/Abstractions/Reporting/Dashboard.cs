using Auxilia.Contracts.Reporting;

namespace Auxilia.Application.Abstractions.Reporting;

/// <summary>The period of the dashboard charts (Q43: week, month, year and all work).</summary>
public enum DashboardPeriod
{
    Week,
    Month,
    Year,
    All,
}

/// <summary>
/// What a contributor knows: the period with its first local day (<c>null</c> for all), today and now in the tenant
/// time zone.
/// </summary>
public sealed record DashboardContext(DashboardPeriod Period, DateOnly? From, DateOnly Today, DateTimeOffset Now, TimeZoneInfo Zone)
{
    /// <summary>The instant <see cref="From"/> starts, for instant columns.</summary>
    public DateTimeOffset? FromInstant => From is { } day
        ? new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Zone), TimeSpan.Zero)
        : null;

    /// <summary>The instant a local day starts.</summary>
    public DateTimeOffset StartOf(DateOnly day) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Zone), TimeSpan.Zero);
}

/// <summary>Cards, charts and lists of one module, in the module's order (<see cref="IDashboardContributor.Order"/>).</summary>
public sealed record DashboardContribution(
    IReadOnlyList<DashboardCardResponse> Cards,
    IReadOnlyList<DashboardChartResponse> Charts,
    IReadOnlyList<DashboardListResponse> Lists)
{
    public static readonly DashboardContribution Empty = new([], [], []);
}

/// <summary>
/// A module's part of the role dashboards (F27), registered by the module: it decides by the caller's roles and
/// permissions what to show, and counts with its own visibility rules (F10 for cases).
/// </summary>
public interface IDashboardContributor
{
    /// <summary>Position of the module's widgets (lower first).</summary>
    int Order { get; }

    Task<DashboardContribution> ContributeAsync(DashboardContext context, CancellationToken cancellationToken);
}
