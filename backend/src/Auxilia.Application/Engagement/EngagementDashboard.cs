using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Engagement;
using Auxilia.Application.Abstractions.Reporting;
using Auxilia.Contracts.Engagement;
using Auxilia.Contracts.Reporting;
using Auxilia.Domain.Engagement;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Engagement;

/// <summary>
/// Requests and tasks on the dashboards (F27): staff see the pending requests of their inbox (Administrators: the
/// office), their open and overdue tasks and, in the "today" list, the tasks due today or before (B-26).
/// </summary>
internal sealed class EngagementDashboard(
    IRequestQueryService requests, ITaskDataFactory tasks, IPermissionAccess permissions, ICurrentUser currentUser) : IDashboardContributor
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

        var cards = new List<DashboardCardResponse>();
        var lists = new List<DashboardListResponse>();
        var pending = await requests.ListAsync(new RequestListQuery("received", "Pending", null, null, 1, 1), cancellationToken);
        if (pending.IsSuccess)
        {
            cards.Add(new DashboardCardResponse("pendingRequests", "app.dashboard.pendingRequests", null, pending.Value.TotalCount, null, "/requests?box=received&filter[status]=Pending"));
        }

        if (currentUser.UserId is { } me && await permissions.HasAsync(EngagementPermissions.ViewTasks, cancellationToken))
        {
            await using var store = await tasks.OpenAsync(cancellationToken);
            var (open, overdue) = await store.CountOpenAsync(me, context.Today, cancellationToken);
            cards.Add(new DashboardCardResponse("openTasks", "app.dashboard.openTasks", null, open, null, "/tasks?filter[status]=Open"));
            cards.Add(new DashboardCardResponse("overdueTasks", "app.dashboard.overdueTasks", null, overdue, null, "/tasks?filter[due]=today"));
            var (due, _) = await store.PageAsync(
                new TaskFilter(null, me, TaskItemStatus.Open, null, null, context.Today, TaskSort.DueOn, false, 0, 5), cancellationToken);
            lists.Add(new DashboardListResponse(
                "todayTasks",
                "app.dashboard.todayTasks",
                [.. due.Select(task => new DashboardItemResponse(
                    task.Id,
                    task.Title,
                    task.ClientName,
                    task.DueOn is { } dueOn ? context.StartOf(dueOn) : null,
                    task.DueOn < context.Today ? "app.tasks.overdue" : null,
                    $"/tasks?open={task.Id}"))]));
        }

        return new DashboardContribution(cards, [], lists);
    }
}
