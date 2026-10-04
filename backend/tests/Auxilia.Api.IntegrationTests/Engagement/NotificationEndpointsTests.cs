using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Auxilia.Api.IntegrationTests.Scheduling;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Engagement;
using Auxilia.Contracts.Scheduling;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Api.IntegrationTests.Engagement;

/// <summary>
/// B-19 over HTTP (F16, Q12): producers (appointments, requests to the office) create notifications for the other
/// party with a deep link; the owner lists, counts, reads one or all and deletes them; preferences per kind switch the
/// in-app notification off.
/// </summary>
public sealed class NotificationEndpointsTests(AppointmentEndpointsTests.Factory factory) : IClassFixture<AppointmentEndpointsTests.Factory>
{
    private const string Password = "A long Passw0rd for tests!";

    private static readonly DateOnly Day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(25);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAppointment_NotifiesTheClient_WhoReadsAndDeletesIt()
    {
        var (employeeId, employee) = await EmployeeAsync();
        var (clientId, client) = await ClientAsync(employeeId);

        var scheduled = await CreatedAsync<AppointmentResponse>(employee, "/api/v1/appointments",
            new ScheduleAppointmentRequest(clientId, null, Day, new TimeOnly(10, 0), null, null, null, null));
        await CreatedAsync<AppointmentResponse>(employee, "/api/v1/appointments",
            new ScheduleAppointmentRequest(clientId, null, Day, new TimeOnly(12, 0), null, null, null, null));

        // The actor is never notified.
        (await GetAsync<UnreadNotificationsResponse>(employee, "/api/v1/notifications/unread-count")).Count.ShouldBe(0);
        (await GetAsync<UnreadNotificationsResponse>(client, "/api/v1/notifications/unread-count")).Count.ShouldBe(2);
        var list = await GetAsync<PagedResponse<NotificationResponse>>(client, "/api/v1/notifications");
        var latest = list.Items[0];
        var first = list.Items.Single(item => item.EntityId == scheduled.Id);
        (first.Kind, first.Link, first.IsRead, first.Parameters.GetProperty("when").GetString())
            .ShouldBe(("appointment.scheduled", $"/appointments?open={scheduled.Id}", false, $"{Day:dd/MM/yyyy} 10:00"));

        // Another user's notification does not exist for the employee.
        (await SendAsync(HttpMethod.Post, $"/api/v1/notifications/{first.Id}/read", employee)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await SendAsync(HttpMethod.Post, $"/api/v1/notifications/{first.Id}/read", client)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetAsync<PagedResponse<NotificationResponse>>(client, "/api/v1/notifications?unreadOnly=true")).Items.Select(item => item.Id).ShouldBe([latest.Id]);
        (await SendAsync(HttpMethod.Post, "/api/v1/notifications/read-all", client)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetAsync<UnreadNotificationsResponse>(client, "/api/v1/notifications/unread-count")).Count.ShouldBe(0);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/notifications/{latest.Id}", client)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetAsync<PagedResponse<NotificationResponse>>(client, "/api/v1/notifications")).TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Preferences_TurnTheInAppNotificationOff()
    {
        var (employeeId, employee) = await EmployeeAsync();
        var (clientId, client) = await ClientAsync(employeeId);

        var defaults = await GetAsync<NotificationPreferenceResponse[]>(client, "/api/v1/notifications/preferences");
        defaults.ShouldContain(new NotificationPreferenceResponse("appointment.scheduled", true, false));

        using (var saved = await SendAsync(HttpMethod.Put, "/api/v1/notifications/preferences", client,
                   new SetNotificationPreferencesRequest([new("appointment.scheduled", false, false)])))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await saved.Content.ReadFromJsonAsync<NotificationPreferenceResponse[]>(Ct))!
                .ShouldContain(new NotificationPreferenceResponse("appointment.scheduled", false, false));
        }

        (await SendAsync(HttpMethod.Put, "/api/v1/notifications/preferences", client,
            new SetNotificationPreferencesRequest([new("workout.plan", true, true)]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await CreatedAsync<AppointmentResponse>(employee, "/api/v1/appointments",
            new ScheduleAppointmentRequest(clientId, null, Day.AddDays(1), new TimeOnly(9, 0), null, null, null, null));
        (await GetAsync<UnreadNotificationsResponse>(client, "/api/v1/notifications/unread-count")).Count.ShouldBe(0);
    }

    [Fact]
    public async Task OfficeRequests_NotifyEveryAdministrator()
    {
        var (_, adminName) = await factory.AddUserAsync([TenantRole.Administrator], Password);
        var admin = await factory.SignInAsync(adminName, Password, Ct);
        var (_, employee) = await EmployeeAsync();

        var request = await CreatedAsync<RequestResponse>(employee, "/api/v1/requests", new CreateRequestRequest("General", "Badge", "My badge is broken", null));

        var notifications = await GetAsync<PagedResponse<NotificationResponse>>(admin, "/api/v1/notifications?pageSize=100");
        var notified = notifications.Items.Single(item => item.EntityId == request.Id);
        (notified.Kind, notified.Link, notified.Parameters.GetProperty("subject").GetString()).ShouldBe(("request.created", $"/requests?open={request.Id}", "Badge"));
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

    private async Task<T> CreatedAsync<T>(string token, string path, object request)
    {
        using var created = await SendAsync(HttpMethod.Post, path, token, request);
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
