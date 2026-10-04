using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Auxilia.Api.IntegrationTests.Scheduling;
using Auxilia.Contracts.Cases;
using Auxilia.Contracts.Reporting;
using Auxilia.Contracts.Scheduling;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Api.IntegrationTests.Reporting;

/// <summary>
/// B-23 over HTTP (F27): the dashboard of each role from the real queries (case figures in SQL, appointment counts),
/// every period, and the dashboards of the employee and the client.
/// </summary>
public sealed class DashboardEndpointsTests(AppointmentEndpointsTests.Factory factory) : IClassFixture<AppointmentEndpointsTests.Factory>
{
    private const string Password = "A long Passw0rd for tests!";

    private static readonly DateOnly Day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Administrator_SeesTotalsCasesRevenueAndAppointments_ForEveryPeriod()
    {
        var admin = await SignInAsync(TenantRole.Administrator);
        var (employeeId, employee) = await EmployeeAsync();
        var (clientId, client) = await ClientAsync(employeeId);
        var service = await CreatedAsync<ServiceResponse>(admin, "/api/v1/services", new CreateServiceRequest("Dashboard " + Guid.NewGuid().ToString("N")[..8], null, 120m, 30, null, null));
        var opened = await CreatedAsync<CaseResponse>(admin, "/api/v1/cases",
            new OpenCaseRequest(clientId, service.Id, null, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5), null, null, null));
        (await SendAsync(HttpMethod.Post, $"/api/v1/cases/{opened.Id}/payments", admin, new AddCasePaymentRequest(40m, null, null))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await CreatedAsync<AppointmentResponse>(employee, "/api/v1/appointments", new ScheduleAppointmentRequest(clientId, null, Day, new TimeOnly(9, 0), null, null, null, null));

        foreach (var period in new[] { "week", "month", "year", "all" })
        {
            var dashboard = await GetAsync<DashboardResponse>(admin, $"/api/v1/dashboard?period={period}");
            dashboard.Period.ShouldBe(period);
            dashboard.Cards.Select(card => card.Key).ShouldContain("totalEmployees");
            dashboard.Cards.Select(card => card.Key).ShouldContain("openCases");
            dashboard.Charts.Select(chart => chart.Key).ShouldBe(["casesPerService", "revenuePerMonth", "appointmentsPerDay", "appointmentStatuses"]);
        }

        var all = await GetAsync<DashboardResponse>(admin, "/api/v1/dashboard?period=all");
        all.Charts.Single(chart => chart.Key == "casesPerService").Points.ShouldContain(point => point.Label == service.Name && point.Value == 1);
        all.Charts.Single(chart => chart.Key == "revenuePerMonth").Points.Sum(point => point.Value).ShouldBeGreaterThanOrEqualTo(40m);
        all.Lists.Single(list => list.Key == "casesDueSoon").Items.Select(item => item.Id).ShouldContain(opened.Id);
        (await SendAsync(HttpMethod.Get, "/api/v1/dashboard?period=decade", admin)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var mine = await GetAsync<DashboardResponse>(employee, "/api/v1/dashboard");
        mine.Cards.Single(card => card.Key == "myClients").Value.ShouldBe(1);
        mine.Cards.Single(card => card.Key == "upcomingAppointments").Value.ShouldBe(1);
        mine.Lists.Select(list => list.Key).ShouldContain("globalAppointments");

        // In the test tenant the cases module is for staff only: the client sees its appointments, no case card.
        var own = await GetAsync<DashboardResponse>(client, "/api/v1/dashboard");
        own.Cards.Select(card => card.Key).ShouldBe(["upcomingAppointments"]);
        own.Cards.Single().Value.ShouldBe(1);
        own.Charts.ShouldBeEmpty();
        own.Lists.Select(list => list.Key).ShouldBe(["appointmentsToday", "nextAppointments"]);
    }

    private async Task<string> SignInAsync(TenantRole role)
    {
        var (_, userName) = await factory.AddUserAsync([role], Password);
        return await factory.SignInAsync(userName, Password, Ct);
    }

    private async Task<(Guid UserId, string Token)> EmployeeAsync()
    {
        var (userId, userName) = await factory.AddUserAsync([TenantRole.Employee], Password);
        return (userId, await factory.SignInAsync(userName, Password, Ct));
    }

    private async Task<(Guid ClientId, string Token)> ClientAsync(Guid? employeeUserId)
    {
        var (clientId, userName) = await factory.AddClientAsync(employeeUserId, Password);
        return (clientId, await factory.SignInAsync(userName, Password, Ct));
    }

    private async Task<T> CreatedAsync<T>(string token, string path, object body)
    {
        using var created = await SendAsync(HttpMethod.Post, path, token, body);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        return (await created.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private async Task<T> GetAsync<T>(string token, string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path, token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? body = null)
    {
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request, Ct);
    }
}
