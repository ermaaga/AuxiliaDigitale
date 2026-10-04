using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Cases;
using Auxilia.Application.Abstractions.Reporting;
using Auxilia.Application.Abstractions.Scheduling;
using Auxilia.Application.Cases;
using Auxilia.Application.Configuration.Public;
using Auxilia.Application.Directory;
using Auxilia.Application.Engagement;
using Auxilia.Application.Reporting;
using Auxilia.Application.Scheduling;
using Auxilia.Application.Tests.Cases;
using Auxilia.Application.Tests.Directory;
using Auxilia.Application.Tests.Identity;
using Auxilia.Application.Tests.Scheduling;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Directory;
using Auxilia.Contracts.Engagement;
using Auxilia.Contracts.Reporting;
using Auxilia.Diagnostics;
using Auxilia.Domain.Scheduling;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using NSubstitute;

namespace Auxilia.Application.Tests.Reporting;

public sealed class DashboardTests
{
    private static readonly Guid Me = Guid.CreateVersion7();

    // ManualTimeProvider: 2026-09-30 08:00 UTC = 10:00 in Rome.
    private static readonly DateOnly Today = new(2026, 9, 30);

    private readonly ICurrentUser caller = Substitute.For<ICurrentUser>();
    private readonly IPermissionAccess permissions = Substitute.For<IPermissionAccess>();
    private readonly ManualTimeProvider clock = new();

    public DashboardTests()
    {
        CallAs(TenantRole.Administrator);
        permissions.HasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DashboardContext Context(DashboardPeriod period = DashboardPeriod.Month) =>
        new(period, DashboardQueryService.From(period, Today), Today, new DateTimeOffset(2026, 9, 30, 8, 0, 0, TimeSpan.Zero), TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome"));

    private void CallAs(params TenantRole[] roles)
    {
        caller.ActorType.Returns(ActorType.User);
        caller.UserId.Returns(Me);
        caller.Roles.Returns(roles);
    }

    [Fact]
    public async Task Dashboard_ParsesThePeriod_AndJoinsTheModulesInOrder()
    {
        var second = Substitute.For<IDashboardContributor>();
        second.Order.Returns(20);
        second.ContributeAsync(Arg.Any<DashboardContext>(), Arg.Any<CancellationToken>())
            .Returns(new DashboardContribution([new DashboardCardResponse("b", "b", null, 2, null, null)], [], []));
        var first = Substitute.For<IDashboardContributor>();
        first.Order.Returns(10);
        first.ContributeAsync(Arg.Any<DashboardContext>(), Arg.Any<CancellationToken>())
            .Returns(new DashboardContribution([new DashboardCardResponse("a", "a", null, 1, null, null)], [], []));
        var dashboard = new DashboardQueryService([second, first], SessionSettings.Tenant(), clock);

        var result = (await dashboard.GetAsync("Year", Ct)).Value;

        (result.Period, string.Join(",", result.Cards.Select(card => card.Key))).ShouldBe(("year", "a,b"));
        await first.Received().ContributeAsync(Arg.Is<DashboardContext>(context => context.From == new DateOnly(2026, 1, 1) && context.Today == Today), Arg.Any<CancellationToken>());
        (await dashboard.GetAsync(null, Ct)).Value.Period.ShouldBe("month");
        (await dashboard.GetAsync("decade", Ct)).Error!.ValidationErrors.Keys.ShouldBe(["period"]);
    }

    [Fact]
    public void Periods_StartOnTheirFirstDay()
    {
        DashboardQueryService.From(DashboardPeriod.Week, Today).ShouldBe(new DateOnly(2026, 9, 24));
        DashboardQueryService.From(DashboardPeriod.Month, Today).ShouldBe(new DateOnly(2026, 9, 1));
        DashboardQueryService.From(DashboardPeriod.Year, Today).ShouldBe(new DateOnly(2026, 1, 1));
        DashboardQueryService.From(DashboardPeriod.All, Today).ShouldBeNull();
        Context().FromInstant.ShouldBe(new DateTimeOffset(2026, 8, 31, 22, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Cases_StaffSeeCountsChartsAndDueSoon_ClientsTheirActiveCase()
    {
        await using var data = new InMemoryCases();
        var due = new CaseDashboardItem(Guid.CreateVersion7(), "2026-00007", "Mario Rossi", "730", Today.AddDays(3));
        data.Dashboard = new CaseDashboard(4, [("730", 3), ("ISEE", 1)], [(2026, 9, 150m)], [due], due);
        var cases = new CasesDashboard(data, new CaseAccessPolicy(caller, data), permissions, caller);

        var staff = await cases.ContributeAsync(Context(), Ct);
        staff.Cards.ShouldHaveSingleItem().ShouldBe(new DashboardCardResponse("openCases", "app.dashboard.openCases", null, 4, null, "/cases"));
        staff.Charts.Select(chart => chart.Key).ShouldBe(["casesPerService", "revenuePerMonth"]);
        staff.Charts[1].Points.ShouldHaveSingleItem().ShouldBe(new DashboardPointResponse("2026-09", null, 150m));
        staff.Lists.ShouldHaveSingleItem().Items.ShouldHaveSingleItem().Link.ShouldBe($"/cases/{due.Id}");
        data.LastDashboardScope!.Everything.ShouldBeTrue();

        CallAs(TenantRole.Employee);
        (await cases.ContributeAsync(Context(), Ct)).Charts.Select(chart => chart.Key).ShouldBe(["casesPerService"]);

        CallAs(TenantRole.Client);
        var client = await cases.ContributeAsync(Context(), Ct);
        client.Cards.ShouldHaveSingleItem().ShouldBe(new DashboardCardResponse("activeCase", "app.dashboard.activeCase", "730", 4, "2026-10-03", $"/cases/{due.Id}"));
        client.Charts.ShouldBeEmpty();

        permissions.HasAsync(CasesPermissions.ViewCases, Arg.Any<CancellationToken>()).Returns(false);
        (await cases.ContributeAsync(Context(), Ct)).ShouldBe(DashboardContribution.Empty);
    }

    [Fact]
    public async Task Appointments_StaffSeeChartsAndLists_ClientsTheirNextOnes()
    {
        await using var data = new InMemoryAppointments();
        var now = clock.GetUtcNow();
        foreach (var (days, status) in new[] { (1, AppointmentStatus.Approved), (1, AppointmentStatus.Pending), (2, AppointmentStatus.Cancelled) })
        {
            var appointment = Appointment.Schedule(Guid.CreateVersion7(), Guid.CreateVersion7(), Me, new AppointmentSlot(now.AddDays(days), 60, null, true, "{}"), Me, now).Value;
            if (status == AppointmentStatus.Cancelled)
            {
                appointment.Cancel(Me, null, now);
            }

            data.Appointments.Add(appointment);
        }

        var scheduling = new SchedulingDashboard(data, new AppointmentAccessPolicy(caller, data), permissions);

        var staff = await scheduling.ContributeAsync(Context(), Ct);
        // Scheduled appointments are approved: two open (approved), one cancelled.
        staff.Cards.ShouldHaveSingleItem().Value.ShouldBe(2);
        staff.Charts.Select(chart => chart.Key).ShouldBe(["appointmentsPerDay", "appointmentStatuses"]);
        staff.Charts[0].Points.Select(point => (point.Label, point.Value)).ShouldBe([("2026-10-01", 2m), ("2026-10-02", 1m)]);
        staff.Charts[1].Points.Single(point => point.Label == "Cancelled").Value.ShouldBe(1);
        staff.Lists.Select(list => list.Key).ShouldBe(["appointmentsToday", "recentAppointments"]);

        CallAs(TenantRole.Employee);
        (await scheduling.ContributeAsync(Context(), Ct)).Lists.Select(list => list.Key).ShouldBe(["appointmentsToday", "globalAppointments"]);

        CallAs(TenantRole.Client);
        var client = await scheduling.ContributeAsync(Context(), Ct);
        client.Charts.ShouldBeEmpty();
        client.Lists.Select(list => list.Key).ShouldBe(["appointmentsToday", "nextAppointments"]);
        client.Lists[1].Items.ShouldAllBe(item => item.StatusKey != "Cancelled");
    }

    [Fact]
    public async Task Directory_AdministratorsSeeTotals_EmployeesTheirClientsAndCounters()
    {
        var clients = Substitute.For<IClientQueryService>();
        clients.ListAsync(Arg.Any<ClientListQuery>(), Arg.Any<CancellationToken>()).Returns(Paged<ClientListItemResponse>(12));
        var employees = Substitute.For<IEmployeeQueryService>();
        employees.ListAsync(Arg.Any<EmployeeListQuery>(), Arg.Any<CancellationToken>()).Returns(Paged<EmployeeListItemResponse>(3));
        var registrations = Substitute.For<IRegistrationQueryService>();
        registrations.ListAsync(Arg.Any<RegistrationListQuery>(), Arg.Any<CancellationToken>()).Returns(Paged<RegistrationResponse>(2));
        var catalog = Substitute.For<ICustomFieldCatalog>();
        catalog.DashboardCountersAsync("client", Arg.Any<CancellationToken>()).Returns([new DashboardCounterField("caf", "CAF")]);
        await using var data = new InMemoryClientData();
        data.Flagged["caf"] = 5;
        var directory = new DirectoryDashboard(clients, employees, registrations, data, catalog, permissions, caller);

        var admin = await directory.ContributeAsync(Context(), Ct);
        admin.Cards.Select(card => (card.Key, card.Value)).ShouldBe([("totalEmployees", 3L), ("totalClients", 12L), ("customField.caf", 5L), ("pendingRegistrations", 2L)]);
        admin.Cards[2].Label.ShouldBe("CAF");

        CallAs(TenantRole.Employee);
        var employee = await directory.ContributeAsync(Context(), Ct);
        employee.Cards.Select(card => card.Key).ShouldBe(["myClients", "customField.caf", "pendingRegistrations"]);
        await clients.Received().ListAsync(Arg.Is<ClientListQuery>(query => query.View == "mine" && query.PageSize == 1), Arg.Any<CancellationToken>());
        await registrations.Received().ListAsync(Arg.Is<RegistrationListQuery>(query => query.Status == "Pending"), Arg.Any<CancellationToken>());

        CallAs(TenantRole.Client);
        (await directory.ContributeAsync(Context(), Ct)).Cards.ShouldBeEmpty();
    }

    [Fact]
    public async Task Requests_StaffSeeThePendingOnes()
    {
        var requests = Substitute.For<IRequestQueryService>();
        requests.ListAsync(Arg.Any<RequestListQuery>(), Arg.Any<CancellationToken>()).Returns(Paged<RequestListItemResponse>(7));
        var engagement = new EngagementDashboard(requests, permissions, caller);

        (await engagement.ContributeAsync(Context(), Ct)).Cards.ShouldHaveSingleItem().Value.ShouldBe(7);
        await requests.Received().ListAsync(Arg.Is<RequestListQuery>(query => query.Box == "received" && query.Status == "Pending"), Arg.Any<CancellationToken>());

        CallAs(TenantRole.Client);
        (await engagement.ContributeAsync(Context(), Ct)).ShouldBe(DashboardContribution.Empty);
    }

    private static Result<PagedResponse<T>> Paged<T>(long total) => new PagedResponse<T>([], 1, 1, total);
}
