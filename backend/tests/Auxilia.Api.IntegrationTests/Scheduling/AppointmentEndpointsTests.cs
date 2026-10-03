using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Identity;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Scheduling;
using Auxilia.Diagnostics;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.Persistence.Tenant;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace Auxilia.Api.IntegrationTests.Scheduling;

/// <summary>
/// B-16 over HTTP (F13, Q19–Q22): staff schedule for the clients in charge, clients request and cancel, approve,
/// complete, conflicts, lists and calendars in the tenant time zone (Europe/Rome), soft delete keeping the history.
/// </summary>
public sealed class AppointmentEndpointsTests(AppointmentEndpointsTests.Factory factory) : IClassFixture<AppointmentEndpointsTests.Factory>
{
    private const string Password = "A long Passw0rd for tests!";

    /// <summary>A day comfortably in the future in any time zone.</summary>
    private static readonly DateOnly Day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Employee_SchedulesForTheClientsInCharge_AndTheClientSeesAndCancels()
    {
        var (employeeId, employee) = await EmployeeAsync();
        var (clientId, client) = await ClientAsync(employeeId);
        var (otherClientId, _) = await ClientAsync(null);

        var scheduled = await CreatedAsync(employee, "/api/v1/appointments",
            new ScheduleAppointmentRequest(clientId, null, Day, new TimeOnly(9, 0), null, "first visit", null, null));
        (scheduled.Status, scheduled.Employee.UserId, scheduled.Client.Id, scheduled.Date, scheduled.Time, scheduled.DurationMinutes, scheduled.RequestedByClient)
            .ShouldBe(("Approved", employeeId, clientId, Day, new TimeOnly(9, 0), 60, false));
        (scheduled.EndsAt - scheduled.StartsAt).ShouldBe(TimeSpan.FromHours(1));
        scheduled.History.ShouldHaveSingleItem().ToStatus.ShouldBe("Approved");

        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, "/api/v1/appointments", employee,
                new ScheduleAppointmentRequest(otherClientId, null, Day, new TimeOnly(9, 0), null, null, null, null)),
            HttpStatusCode.Forbidden, EventCodes.Scheduling.AppointmentClientNotInCharge);
        using (var past = await SendAsync(HttpMethod.Post, "/api/v1/appointments", employee,
                   new ScheduleAppointmentRequest(clientId, null, new DateOnly(2020, 1, 1), new TimeOnly(9, 0), 2, null, null, null)))
        {
            past.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await past.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").EnumerateObject().Select(field => field.Name)
                .ShouldBe(["startsAt", "durationMinutes"], ignoreOrder: true);
        }

        var mine = await GetAsync<PagedResponse<AppointmentListItemResponse>>(client, "/api/v1/appointments");
        mine.Items.ShouldHaveSingleItem().Id.ShouldBe(scheduled.Id);
        var detail = await GetAsync<AppointmentResponse>(client, $"/api/v1/appointments/{scheduled.Id}");
        (detail.CanManage, detail.CanCancel, detail.CanDelete, detail.HasConflict).ShouldBe((false, true, false, false));
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, $"/api/v1/appointments/{scheduled.Id}/complete", client),
            HttpStatusCode.Forbidden, EventCodes.Identity.PermissionDenied);

        var cancelled = await ChangeAsync(client, scheduled.Id, "cancel", new ChangeAppointmentStatusRequest("cannot come"));
        (cancelled.Status, cancelled.CanCancel).ShouldBe(("Cancelled", false));
        cancelled.History[^1].Note.ShouldBe("cannot come");
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Put, $"/api/v1/appointments/{scheduled.Id}", employee,
                new UpdateAppointmentRequest(Day, new TimeOnly(10, 0), 60, null, true, null)),
            HttpStatusCode.Conflict, EventCodes.Scheduling.AppointmentClosed);
    }

    [Fact]
    public async Task Client_RequestsWithAnyEmployee_TheEmployeeApprovesAndCompletes()
    {
        var (employeeId, employee) = await EmployeeAsync();
        var (otherEmployeeId, _) = await EmployeeAsync();
        var (_, client) = await ClientAsync(employeeId);

        var employees = await GetAsync<AppointmentEmployeeChoiceResponse[]>(client, "/api/v1/appointments/employees");
        employees.Single(choice => choice.UserId == employeeId).InCharge.ShouldBeTrue();
        employees.Single(choice => choice.UserId == otherEmployeeId).InCharge.ShouldBeFalse();
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Get, "/api/v1/appointments/employees", employee),
            HttpStatusCode.Forbidden, EventCodes.Identity.PermissionDenied);

        var requested = await CreatedAsync(client, "/api/v1/appointments/requests", new RequestAppointmentRequest(employeeId, Day, new TimeOnly(15, 0), 30, "tax return"));
        (requested.Status, requested.RequestedByClient, requested.DurationMinutes).ShouldBe(("Pending", true, 30));
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, "/api/v1/appointments/requests", employee,
                new RequestAppointmentRequest(employeeId, Day, new TimeOnly(15, 0), null, null)),
            HttpStatusCode.Forbidden, EventCodes.Identity.PermissionDenied);

        (await GetAsync<AppointmentResponse>(employee, $"/api/v1/appointments/{requested.Id}")).CanManage.ShouldBeTrue();
        (await ChangeAsync(employee, requested.Id, "approve", null)).Status.ShouldBe("Approved");
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, $"/api/v1/appointments/{requested.Id}/reject", employee),
            HttpStatusCode.Conflict, EventCodes.Scheduling.AppointmentNotPending);

        var completed = await ChangeAsync(employee, requested.Id, "complete", new ChangeAppointmentStatusRequest("done"));
        completed.History.Select(change => change.ToStatus).ShouldBe(["Pending", "Approved", "Completed"]);
        completed.History[^1].ChangedBy!.UserId.ShouldBe(employeeId);
    }

    [Fact]
    public async Task Conflicts_AreWarnings_ForTheSameEmployee()
    {
        var (employeeId, employee) = await EmployeeAsync();
        var (clientId, _) = await ClientAsync(employeeId);
        var date = Day.AddDays(1);

        var first = await CreatedAsync(employee, "/api/v1/appointments", new ScheduleAppointmentRequest(clientId, null, date, new TimeOnly(9, 0), 60, null, false, null));
        var overlapping = await CreatedAsync(employee, "/api/v1/appointments", new ScheduleAppointmentRequest(clientId, null, date, new TimeOnly(9, 30), 60, null, false, null));
        var after = await CreatedAsync(employee, "/api/v1/appointments", new ScheduleAppointmentRequest(clientId, null, date, new TimeOnly(10, 30), 30, null, false, null));

        (await GetAsync<AppointmentResponse>(employee, $"/api/v1/appointments/{overlapping.Id}")).HasConflict.ShouldBeTrue();
        (await GetAsync<AppointmentResponse>(employee, $"/api/v1/appointments/{after.Id}")).HasConflict.ShouldBeFalse();

        var conflicts = await GetAsync<AppointmentConflictResponse[]>(employee,
            $"/api/v1/appointments/conflicts?employeeUserId={employeeId}&date={date:yyyy-MM-dd}&time=09:15&durationMinutes=30&excludeId={first.Id}");
        conflicts.Select(conflict => conflict.Id).ShouldBe([overlapping.Id]);

        // A cancelled appointment no longer takes the time.
        await ChangeAsync(employee, overlapping.Id, "cancel", null);
        (await GetAsync<AppointmentResponse>(employee, $"/api/v1/appointments/{first.Id}")).HasConflict.ShouldBeFalse();
    }

    [Fact]
    public async Task ListsAndCalendars_FollowTheScope_LocalDays_AndTheGlobalFlag()
    {
        var admin = await SignInAsync(TenantRole.Administrator);
        var (employeeId, employee) = await EmployeeAsync();
        var (otherEmployeeId, otherEmployee) = await EmployeeAsync();
        var (clientId, client) = await ClientAsync(employeeId);
        var date = Day.AddDays(2);

        // Late evening in Rome: still the same local day although it is close to midnight UTC.
        var shared = await CreatedAsync(admin, "/api/v1/appointments", new ScheduleAppointmentRequest(clientId, null, date, new TimeOnly(23, 30), 20, null, true, null));
        var hidden = await CreatedAsync(admin, "/api/v1/appointments", new ScheduleAppointmentRequest(clientId, otherEmployeeId, date, new TimeOnly(8, 0), 60, null, false, null));
        hidden.Employee.UserId.ShouldBe(otherEmployeeId);

        var day = await GetAsync<PagedResponse<AppointmentListItemResponse>>(admin, $"/api/v1/appointments?filter[clientId]={clientId}&from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}&sort=startsAt");
        day.Items.Select(item => item.Id).ShouldBe([hidden.Id, shared.Id]);
        (await GetAsync<PagedResponse<AppointmentListItemResponse>>(admin, $"/api/v1/appointments?filter[clientId]={clientId}&from={date.AddDays(1):yyyy-MM-dd}"))
            .Items.ShouldBeEmpty();

        // The employee lists their own; the other employee's private appointment is not found; the shared one is on the global calendar.
        (await GetAsync<PagedResponse<AppointmentListItemResponse>>(employee, "/api/v1/appointments")).Items.Select(item => item.Id).ShouldBe([shared.Id]);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Get, $"/api/v1/appointments/{hidden.Id}", employee), HttpStatusCode.NotFound,
            EventCodes.Scheduling.AppointmentNotFound);
        var global = await GetAsync<AppointmentCalendarResponse>(otherEmployee, $"/api/v1/appointments/calendar?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}&global=true");
        global.TimeZone.ShouldBe("Europe/Rome");
        global.Items.Select(item => item.Id).ShouldBe([shared.Id]);
        (await GetAsync<AppointmentResponse>(otherEmployee, $"/api/v1/appointments/{shared.Id}")).CanManage.ShouldBeFalse();

        var own = await GetAsync<AppointmentCalendarResponse>(client, $"/api/v1/appointments/calendar?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");
        own.Items.Select(item => item.Id).ShouldBe([hidden.Id, shared.Id]);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Get, $"/api/v1/appointments/calendar?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}&global=true", client),
            HttpStatusCode.Forbidden, EventCodes.Identity.PermissionDenied);
        (await SendAsync(HttpMethod.Get, $"/api/v1/appointments/calendar?from={date:yyyy-MM-dd}&to={date.AddDays(62):yyyy-MM-dd}", admin))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_IsSoft_KeepsTheHistory_AndIsForStaff()
    {
        var (employeeId, employee) = await EmployeeAsync();
        var (clientId, client) = await ClientAsync(employeeId);
        var scheduled = await CreatedAsync(employee, "/api/v1/appointments", new ScheduleAppointmentRequest(clientId, null, Day, new TimeOnly(12, 0), null, null, null, null));
        var moved = await SendJsonAsync<AppointmentResponse>(employee, HttpMethod.Put, $"/api/v1/appointments/{scheduled.Id}",
            new UpdateAppointmentRequest(Day.AddDays(1), new TimeOnly(12, 30), 45, "moved", false, null));
        (moved.Date, moved.Time, moved.DurationMinutes, moved.Notes, moved.ShowInGlobalCalendar).ShouldBe((Day.AddDays(1), new TimeOnly(12, 30), 45, "moved", false));

        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Delete, $"/api/v1/appointments/{scheduled.Id}", client), HttpStatusCode.Forbidden,
            EventCodes.Identity.PermissionDenied);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/appointments/{scheduled.Id}", employee)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Get, $"/api/v1/appointments/{scheduled.Id}", employee)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Factory.HistoryRowsAsync(scheduled.Id)).ShouldBe(1);
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

    private async Task<AppointmentResponse> CreatedAsync(string token, string path, object request)
    {
        using var created = await SendAsync(HttpMethod.Post, path, token, request);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        return (await created.Content.ReadFromJsonAsync<AppointmentResponse>(Ct))!;
    }

    private Task<AppointmentResponse> ChangeAsync(string token, Guid id, string action, object? body) =>
        SendJsonAsync<AppointmentResponse>(token, HttpMethod.Post, $"/api/v1/appointments/{id}/{action}", body);

    private async Task<T> GetAsync<T>(string token, string path) => await SendJsonAsync<T>(token, HttpMethod.Get, path, null);

    private async Task<T> SendJsonAsync<T>(string token, HttpMethod method, string path, object? body)
    {
        using var response = await SendAsync(method, path, token, body);
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

    private static async Task ShouldHaveCodeAsync(HttpResponseMessage response, HttpStatusCode status, int code)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync(Ct));
            (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString().ShouldBe($"AUX-{code}");
        }
    }

    public sealed class Factory : AuthEndpointsTests.Factory
    {
        private static NpgsqlDataSource DataSource() =>
            NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(ApiDatabase.Instance.CatalogConnectionString) { Database = "tenant_a" }.ConnectionString);

        /// <summary>A client of tenant A (person, profile in charge of <paramref name="employeeUserId"/>, account that signs in).</summary>
        public async Task<(Guid ClientId, string UserName)> AddClientAsync(Guid? employeeUserId, string password)
        {
            await using var dataSource = DataSource();
            await using var db = new TenantDbContext(TenantDbContextOptions.Create(dataSource));
            var person = new Person(Guid.CreateVersion7(), "Marco", "Ferri", null);
            db.Set<Person>().Add(person);
            db.Set<ClientProfile>().Add(ClientProfile.Create(person.Id, employeeUserId, DateTimeOffset.UtcNow));
            var userName = "client-" + Guid.NewGuid().ToString("N")[..10];
            var user = User.Create(Guid.CreateVersion7(), person.Id, userName, userName + "@example.test", "it", [TenantRole.Client], isActive: true).Value;
            user.SetPassword(Services.GetRequiredService<IPasswordHasher>().Hash(password), PasswordFormat.Identity, DateTimeOffset.UtcNow);
            db.Set<User>().Add(user);
            await db.SaveChangesAsync();
            return (person.Id, userName);
        }

        /// <summary>Rows of the appointment history, deleted appointment included.</summary>
        public static async Task<int> HistoryRowsAsync(Guid appointmentId)
        {
            await using var dataSource = DataSource();
            await using var command = dataSource.CreateCommand("SELECT count(*) FROM scheduling.appointment_status_history WHERE appointment_id = $1");
            command.Parameters.AddWithValue(appointmentId);
            return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
