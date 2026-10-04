using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Reporting;
using Auxilia.Contracts.Engagement;
using Auxilia.Contracts.Reporting;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Engagement;

/// <summary>Requests on the dashboards (F27): staff see the pending requests of their inbox (Administrators: the office).</summary>
internal sealed class EngagementDashboard(IRequestQueryService requests, IPermissionAccess permissions, ICurrentUser currentUser) : IDashboardContributor
{
    public int Order => 40;

    public async Task<DashboardContribution> ContributeAsync(DashboardContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var isStaff = currentUser.Roles.Contains(TenantRole.Administrator) || currentUser.Roles.Contains(TenantRole.Employee);
        if (!isStaff || !await permissions.HasAsync(EngagementPermissions.ViewRequests, cancellationToken))
        {
            return DashboardContribution.Empty;
        }

        var pending = await requests.ListAsync(new RequestListQuery("received", "Pending", null, null, 1, 1), cancellationToken);
        return pending.IsFailure
            ? DashboardContribution.Empty
            : new DashboardContribution(
                [new DashboardCardResponse("pendingRequests", "app.dashboard.pendingRequests", null, pending.Value.TotalCount, null, "/requests?box=received&filter[status]=Pending")],
                [],
                []);
    }
}
