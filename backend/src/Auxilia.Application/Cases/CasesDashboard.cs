using System.Globalization;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Cases;
using Auxilia.Application.Abstractions.Reporting;
using Auxilia.Contracts.Reporting;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Cases;

/// <summary>
/// Cases on the dashboards (F27, F10 in the counts): staff see the open cases, cases per service and the cases due in
/// the next 30 days; Administrators also the money received per month of start (Q43); a client sees the active case
/// (service and due date).
/// </summary>
internal sealed class CasesDashboard(ICaseDataFactory data, CaseAccessPolicy policy, IPermissionAccess permissions, ICurrentUser currentUser) : IDashboardContributor
{
    public const int DueSoonDays = 30;
    public const int ListSize = 10;

    public int Order => 20;

    public async Task<DashboardContribution> ContributeAsync(DashboardContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!await permissions.HasAsync(CasesPermissions.ViewCases, cancellationToken))
        {
            return DashboardContribution.Empty;
        }

        var scope = await policy.ScopeAsync(cancellationToken);
        await using var store = await data.OpenAsync(cancellationToken);
        var figures = await store.DashboardAsync(scope, context.From, context.Today, context.Today.AddDays(DueSoonDays), ListSize, cancellationToken);
        var isStaff = currentUser.Roles.Contains(TenantRole.Administrator) || currentUser.Roles.Contains(TenantRole.Employee);
        if (!isStaff)
        {
            var latest = figures.Latest;
            return new DashboardContribution(
                [new DashboardCardResponse(
                    "activeCase", "app.dashboard.activeCase", latest?.ServiceName, figures.Open,
                    latest?.DueOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), latest is null ? "/cases" : $"/cases/{latest.Id}")],
                [],
                []);
        }

        var charts = new List<DashboardChartResponse>
        {
            new("casesPerService", "app.dashboard.casesPerService", "bar", figures.PerService.Select(item => new DashboardPointResponse(item.Service, null, item.Count)).ToArray()),
        };
        if (currentUser.Roles.Contains(TenantRole.Administrator))
        {
            charts.Add(new DashboardChartResponse(
                "revenuePerMonth", "app.dashboard.revenuePerMonth", "line",
                figures.Revenue.Select(item => new DashboardPointResponse($"{item.Year:D4}-{item.Month:D2}", null, item.Amount)).ToArray()));
        }

        return new DashboardContribution(
            [new DashboardCardResponse("openCases", "app.dashboard.openCases", null, figures.Open, null, "/cases")],
            charts,
            [new DashboardListResponse(
                "casesDueSoon", "app.dashboard.casesDueSoon",
                figures.DueSoon.Select(item => new DashboardItemResponse(
                    item.Id, $"{item.Number} · {item.ServiceName}", item.ClientName,
                    item.DueOn is { } due ? context.StartOf(due) : null, null, $"/cases/{item.Id}")).ToArray())]);
    }
}
