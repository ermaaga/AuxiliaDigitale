using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Directory;
using Auxilia.Application.Abstractions.Reporting;
using Auxilia.Application.Configuration.Public;
using Auxilia.Contracts.Directory;
using Auxilia.Contracts.Reporting;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Directory;

/// <summary>
/// People on the dashboards (F27): Administrators see total employees and clients, employees their clients; staff
/// who review registrations see the pending ones (Q42: its own label); one counter per boolean client custom field
/// flagged for the dashboard (Q41), over the clients the caller sees.
/// </summary>
internal sealed class DirectoryDashboard(
    IClientQueryService clients,
    IEmployeeQueryService employees,
    IRegistrationQueryService registrations,
    IClientDataFactory data,
    ICustomFieldCatalog customFields,
    IPermissionAccess permissions,
    ICurrentUser currentUser) : IDashboardContributor
{
    public int Order => 10;

    public async Task<DashboardContribution> ContributeAsync(DashboardContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var isAdministrator = currentUser.Roles.Contains(TenantRole.Administrator);
        var isEmployee = currentUser.Roles.Contains(TenantRole.Employee);
        var cards = new List<DashboardCardResponse>();
        if (isAdministrator && await permissions.HasAsync(DirectoryPermissions.ViewEmployees, cancellationToken)
            && await employees.ListAsync(new EmployeeListQuery(null, null, null, null, null, null, null, 1, 1), cancellationToken) is { IsSuccess: true } all)
        {
            cards.Add(new DashboardCardResponse("totalEmployees", "app.dashboard.totalEmployees", null, all.Value.TotalCount, null, "/employees"));
        }

        var canSeeClients = (isAdministrator || isEmployee) && await permissions.HasAsync(DirectoryPermissions.ViewClients, cancellationToken);
        var view = isAdministrator ? "all" : "mine";
        if (canSeeClients
            && await clients.ListAsync(new ClientListQuery(view, null, null, null, null, null, null, null, 1, 1), cancellationToken) is { IsSuccess: true } listed)
        {
            cards.Add(isAdministrator
                ? new DashboardCardResponse("totalClients", "app.dashboard.totalClients", null, listed.Value.TotalCount, null, "/clients?view=all")
                : new DashboardCardResponse("myClients", "app.dashboard.myClients", null, listed.Value.TotalCount, null, "/clients?view=mine"));

            var counters = await customFields.DashboardCountersAsync("client", cancellationToken);
            if (counters.Count > 0)
            {
                await using var store = await data.OpenAsync(cancellationToken);
                foreach (var counter in counters)
                {
                    var count = await store.CountFlaggedAsync(counter.Key, isAdministrator ? null : currentUser.UserId, cancellationToken);
                    cards.Add(new DashboardCardResponse($"customField.{counter.Key}", "app.dashboard.customFieldCounter", counter.Label, count, null, $"/clients?view={view}"));
                }
            }
        }

        if ((isAdministrator || isEmployee) && await permissions.HasAsync(DirectoryPermissions.ReviewRegistrations, cancellationToken)
            && await registrations.ListAsync(new RegistrationListQuery("Pending", null, null, null, null, 1, 1), cancellationToken) is { IsSuccess: true } pending)
        {
            cards.Add(new DashboardCardResponse("pendingRegistrations", "app.dashboard.pendingRegistrations", null, pending.Value.TotalCount, null, null));
        }

        return new DashboardContribution(cards, [], []);
    }
}
