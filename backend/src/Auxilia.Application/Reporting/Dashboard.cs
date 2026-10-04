using Auxilia.Application.Abstractions.Reporting;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Contracts.Reporting;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Reporting;

/// <summary>The dashboard of the caller's role (F27, Q41–Q44): one page, widgets from every visible module.</summary>
public interface IDashboardQueryService
{
    /// <param name="period"><c>week</c>, <c>month</c> (default), <c>year</c>, <c>all</c>.</param>
    Task<Result<DashboardResponse>> GetAsync(string? period, CancellationToken cancellationToken);
}

internal sealed class DashboardQueryService(IEnumerable<IDashboardContributor> contributors, ITenantContext tenant, TimeProvider clock) : IDashboardQueryService
{
    public async Task<Result<DashboardResponse>> GetAsync(string? period, CancellationToken cancellationToken)
    {
        var parsed = DashboardPeriod.Month;
        if (!string.IsNullOrEmpty(period) && (!Enum.TryParse(period, ignoreCase: true, out parsed) || !Enum.IsDefined(parsed)))
        {
            return Errors.Host.ValidationFailed(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["period"] = ["validation.dashboard.period"] });
        }

        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(tenant.Tenant.TimeZone, out var found) ? found : TimeZoneInfo.Utc;
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var context = new DashboardContext(parsed, From(parsed, today), today, now, zone);

        var cards = new List<DashboardCardResponse>();
        var charts = new List<DashboardChartResponse>();
        var lists = new List<DashboardListResponse>();
        foreach (var contributor in contributors.OrderBy(item => item.Order))
        {
            var part = await contributor.ContributeAsync(context, cancellationToken);
            cards.AddRange(part.Cards);
            charts.AddRange(part.Charts);
            lists.AddRange(part.Lists);
        }

        return new DashboardResponse(Name(parsed), cards, charts, lists);
    }

    private static string Name(DashboardPeriod period) => period switch
    {
        DashboardPeriod.Week => "week",
        DashboardPeriod.Month => "month",
        DashboardPeriod.Year => "year",
        _ => "all",
    };

    /// <summary>The first day of the period: the last 7 days, the current month, the current year, or none.</summary>
    public static DateOnly? From(DashboardPeriod period, DateOnly today) => period switch
    {
        DashboardPeriod.Week => today.AddDays(-6),
        DashboardPeriod.Month => new DateOnly(today.Year, today.Month, 1),
        DashboardPeriod.Year => new DateOnly(today.Year, 1, 1),
        _ => null,
    };
}
