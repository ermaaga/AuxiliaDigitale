namespace Auxilia.Contracts.Reporting;

/// <summary>
/// The dashboard of the caller's role (F27): KPI cards (with the link to the filtered list), charts and short lists,
/// each from the module it belongs to; <c>period</c> <c>week</c>, <c>month</c> (default), <c>year</c> or <c>all</c>
/// filters the charts (Q43: every option works).
/// </summary>
public sealed record DashboardResponse(
    string Period,
    IReadOnlyList<DashboardCardResponse> Cards,
    IReadOnlyList<DashboardChartResponse> Charts,
    IReadOnlyList<DashboardListResponse> Lists);

/// <param name="Label">A fixed label (e.g. a custom field's label) instead of <paramref name="LabelKey"/>.</param>
/// <param name="Detail">A second line (e.g. the expiry of the client's active case).</param>
/// <param name="Link">The tenant route of the list behind the number.</param>
public sealed record DashboardCardResponse(string Key, string LabelKey, string? Label, long Value, string? Detail, string? Link);

/// <param name="Kind"><c>bar</c>, <c>line</c> or <c>pie</c>.</param>
public sealed record DashboardChartResponse(string Key, string LabelKey, string Kind, IReadOnlyList<DashboardPointResponse> Points);

/// <param name="Label">The category as it is (a service name, <c>2026-10</c>, <c>2026-10-04</c>…).</param>
/// <param name="LabelKey">The category as a translation key (statuses), else null.</param>
public sealed record DashboardPointResponse(string Label, string? LabelKey, decimal Value);

public sealed record DashboardListResponse(string Key, string LabelKey, IReadOnlyList<DashboardItemResponse> Items);

/// <param name="StatusKey">The status as a translation key, when the item has one.</param>
public sealed record DashboardItemResponse(Guid Id, string Title, string? Subtitle, DateTimeOffset? When, string? StatusKey, string? Link);
