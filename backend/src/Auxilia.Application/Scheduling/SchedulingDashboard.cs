using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Reporting;
using Auxilia.Application.Abstractions.Scheduling;
using Auxilia.Contracts.Reporting;
using Auxilia.Domain.Scheduling;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Scheduling;

/// <summary>
/// Appointments on the dashboards (F27): everybody sees the upcoming ones and today's; staff see appointments per day
/// and the status distribution of the period; Administrators the appointments from yesterday on, employees those of
/// the global calendar still open, clients their next ones.
/// </summary>
internal sealed class SchedulingDashboard(IAppointmentDataFactory data, AppointmentAccessPolicy policy, IPermissionAccess permissions) : IDashboardContributor
{
    public const int ListSize = 10;
    public const int MaxChartRows = 5000;

    public int Order => 30;

    public async Task<DashboardContribution> ContributeAsync(DashboardContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!await permissions.HasAsync(SchedulingPermissions.ViewAppointments, cancellationToken))
        {
            return DashboardContribution.Empty;
        }

        var scope = await policy.ScopeAsync(cancellationToken);
        await using var store = await data.OpenAsync(cancellationToken);
        AppointmentFilter Filter(DateTimeOffset? from, DateTimeOffset? to, bool global = false, int take = ListSize) =>
            new(global ? new AppointmentScope(true, null, null) : scope, null, null, null, from, to, global, AppointmentSort.StartsAt, false, 0, take);

        var ahead = await store.CountByStatusAsync(Filter(context.Now, null), cancellationToken);
        var upcoming = ahead.GetValueOrDefault(AppointmentStatus.Pending) + ahead.GetValueOrDefault(AppointmentStatus.Approved);
        var cards = new List<DashboardCardResponse> { new("upcomingAppointments", "app.dashboard.upcomingAppointments", null, upcoming, null, "/appointments?view=list") };

        var today = (await store.PageAsync(Filter(context.StartOf(context.Today), context.StartOf(context.Today.AddDays(1))), cancellationToken)).Items;
        var lists = new List<DashboardListResponse> { new("appointmentsToday", "app.dashboard.appointmentsToday", today.Select(Item).ToArray()) };
        var charts = new List<DashboardChartResponse>();

        if (policy.IsStaff)
        {
            var period = Filter(context.FromInstant, context.StartOf(context.Today.AddDays(1)));
            var starts = await store.StartsAsync(period, MaxChartRows, cancellationToken);
            charts.Add(new DashboardChartResponse(
                "appointmentsPerDay", "app.dashboard.appointmentsPerDay", "bar",
                starts.GroupBy(start => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, context.Zone).DateTime))
                    .OrderBy(group => group.Key)
                    .Select(group => new DashboardPointResponse(group.Key.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), null, group.Count()))
                    .ToArray()));
            var statuses = await store.CountByStatusAsync(period, cancellationToken);
            charts.Add(new DashboardChartResponse(
                "appointmentStatuses", "app.dashboard.appointmentStatuses", "pie",
                Enum.GetValues<AppointmentStatus>().Select(status => new DashboardPointResponse(status.ToString(), status.ToString(), statuses.GetValueOrDefault(status))).ToArray()));

            var shared = policy.IsAdministrator
                ? (await store.PageAsync(Filter(context.StartOf(context.Today.AddDays(-1)), null), cancellationToken)).Items
                : (await store.PageAsync(Filter(context.Now, null, global: true), cancellationToken)).Items;
            lists.Add(new DashboardListResponse(
                policy.IsAdministrator ? "recentAppointments" : "globalAppointments",
                policy.IsAdministrator ? "app.dashboard.recentAppointments" : "app.dashboard.globalAppointments",
                shared.Select(Item).ToArray()));
        }
        else
        {
            var next = (await store.PageAsync(Filter(context.Now, null, take: 5), cancellationToken)).Items.Where(row => Appointment.IsOpen(row.Status));
            lists.Add(new DashboardListResponse("nextAppointments", "app.dashboard.nextAppointments", next.Select(Item).ToArray()));
        }

        return new DashboardContribution(cards, charts, lists);
    }

    private static DashboardItemResponse Item(AppointmentRow row) =>
        new(row.Id, row.ClientName, row.EmployeeName, row.StartsAt, row.Status.ToString(), $"/appointments?open={row.Id}");
}
