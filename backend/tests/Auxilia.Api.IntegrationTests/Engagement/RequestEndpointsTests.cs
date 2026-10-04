using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Scheduling;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Engagement;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Tenancy;

using Npgsql;

namespace Auxilia.Api.IntegrationTests.Engagement;

/// <summary>
/// B-18 over HTTP (F15, Q16–Q18): clients ask the employee in charge or the office, employees ask the office, threads
/// with replies of both sides, inbox boxes per role, close, soft delete by Administrators keeping the thread.
/// </summary>
public sealed class RequestEndpointsTests(AppointmentEndpointsTests.Factory factory) : IClassFixture<AppointmentEndpointsTests.Factory>
{
    private const string Password = "A long Passw0rd for tests!";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Client_AsksTheEmployeeInCharge_TheThreadGrows_AndCloses()
    {
        var (employeeId, employee) = await EmployeeAsync();
        var (_, otherEmployee) = await EmployeeAsync();
        var (_, client) = await ClientAsync(employeeId);

        var created = await CreatedAsync(client, new CreateRequestRequest("Support", "Invoice", "Where is my invoice?", null));
        (created.Status, created.Type, created.Recipient!.UserId, created.CanReply, created.CanDelete).ShouldBe(("Pending", "Support", employeeId, true, false));
        created.Messages.ShouldHaveSingleItem().Mine.ShouldBeTrue();

        var inbox = await GetAsync<PagedResponse<RequestListItemResponse>>(employee, "/api/v1/requests");
        inbox.Items.ShouldHaveSingleItem().Id.ShouldBe(created.Id);
        (await GetAsync<PagedResponse<RequestListItemResponse>>(otherEmployee, "/api/v1/requests")).Items.ShouldBeEmpty();
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Get, $"/api/v1/requests/{created.Id}", otherEmployee), HttpStatusCode.NotFound,
            EventCodes.Requests.RequestNotFound);

        var replied = await PostAsync(employee, $"/api/v1/requests/{created.Id}/messages", new ReplyToRequestRequest("Sent yesterday"));
        (replied.Status, replied.Messages.Count).ShouldBe(("Responded", 2));
        var followUp = await PostAsync(client, $"/api/v1/requests/{created.Id}/messages", new ReplyToRequestRequest("Got it, thanks"));
        followUp.Status.ShouldBe("Pending");
        followUp.Messages.Select(message => message.Mine).ShouldBe([true, false, true]);

        var sent = await GetAsync<PagedResponse<RequestListItemResponse>>(client, "/api/v1/requests");
        sent.Items.ShouldHaveSingleItem().MessageCount.ShouldBe(3);

        var closed = await PostAsync(employee, $"/api/v1/requests/{created.Id}/close", null);
        (closed.Status, closed.CanReply, closed.ClosedAt is not null).ShouldBe(("Closed", false, true));
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, $"/api/v1/requests/{created.Id}/messages", client, new ReplyToRequestRequest("again")),
            HttpStatusCode.Conflict, EventCodes.Requests.RequestIsClosed);
    }

    [Fact]
    public async Task OfficeRequests_AreForTheAdministrators_WhoAlsoSeeEverything()
    {
        var admin = await SignInAsync(TenantRole.Administrator);
        var (employeeId, employee) = await EmployeeAsync();
        var (_, client) = await ClientAsync(employeeId);
        var (_, lone) = await ClientAsync(null);

        var fromEmployee = await CreatedAsync(employee, new CreateRequestRequest("General", "Holidays", "Can I take Friday off?", true));
        fromEmployee.Recipient.ShouldBeNull();
        var fromLone = await CreatedAsync(lone, new CreateRequestRequest("Information", "Hours", "When are you open?", true));
        fromLone.Recipient.ShouldBeNull();
        var toEmployee = await CreatedAsync(client, new CreateRequestRequest("Appointment", "Visit", "Next week?", null));

        var office = await GetAsync<PagedResponse<RequestListItemResponse>>(admin, "/api/v1/requests?filter[status]=Pending&pageSize=100");
        office.Items.Select(item => item.Id).ShouldContain(fromEmployee.Id);
        office.Items.Select(item => item.Id).ShouldContain(fromLone.Id);
        office.Items.Select(item => item.Id).ShouldNotContain(toEmployee.Id);
        (await GetAsync<PagedResponse<RequestListItemResponse>>(admin, "/api/v1/requests?box=all&pageSize=100")).Items.Select(item => item.Id).ShouldContain(toEmployee.Id);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Get, "/api/v1/requests?box=all", employee), HttpStatusCode.Forbidden, EventCodes.Identity.PermissionDenied);

        (await PostAsync(admin, $"/api/v1/requests/{fromEmployee.Id}/messages", new ReplyToRequestRequest("Approved"))).Status.ShouldBe("Responded");
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, "/api/v1/requests", admin, new CreateRequestRequest("General", "x", "y", null)),
            HttpStatusCode.Forbidden, EventCodes.Identity.PermissionDenied);
        using (var invalid = await SendAsync(HttpMethod.Post, "/api/v1/requests", client, new CreateRequestRequest("Urgent", " ", "", null)))
        {
            invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await invalid.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").EnumerateObject().Select(field => field.Name)
                .ShouldBe(["type", "subject", "message"], ignoreOrder: true);
        }
    }

    [Fact]
    public async Task Delete_IsForAdministrators_AndKeepsTheThread()
    {
        var admin = await SignInAsync(TenantRole.Administrator);
        var (_, employee) = await EmployeeAsync();
        var created = await CreatedAsync(employee, new CreateRequestRequest("Support", "Printer", "It does not print", null));

        (await SendAsync(HttpMethod.Delete, $"/api/v1/requests/{created.Id}", employee)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/requests/{created.Id}", admin)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Get, $"/api/v1/requests/{created.Id}", employee)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await MessageRowsAsync(created.Id)).ShouldBe(1);
    }

    private static async Task<int> MessageRowsAsync(Guid requestId)
    {
        await using var dataSource = NpgsqlDataSource.Create(
            new NpgsqlConnectionStringBuilder(ApiDatabase.Instance.CatalogConnectionString) { Database = "tenant_a" }.ConnectionString);
        await using var command = dataSource.CreateCommand("SELECT count(*) FROM engagement.request_messages WHERE request_id = $1");
        command.Parameters.AddWithValue(requestId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
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

    private async Task<RequestResponse> CreatedAsync(string token, CreateRequestRequest request)
    {
        using var created = await SendAsync(HttpMethod.Post, "/api/v1/requests", token, request);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        return (await created.Content.ReadFromJsonAsync<RequestResponse>(Ct))!;
    }

    private Task<RequestResponse> PostAsync(string token, string path, object? body) => SendJsonAsync<RequestResponse>(token, HttpMethod.Post, path, body);

    private Task<T> GetAsync<T>(string token, string path) => SendJsonAsync<T>(token, HttpMethod.Get, path, null);

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
}
