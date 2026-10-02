using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Host;
using Auxilia.Api.IntegrationTests.Identity;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Directory;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Api.IntegrationTests.Employees;

/// <summary>
/// B-02 over HTTP (F06): employees created by an Administrator, list, detail, edit, sign-in, default employee (Q31)
/// serving new clients, administrator (Q32), specializations, clients in charge, delete, credentials, with the
/// permissions of each role and the tenant boundary.
/// </summary>
public sealed class EmployeeEndpointsTests(EmployeeEndpointsTests.Factory factory) : IClassFixture<EmployeeEndpointsTests.Factory>
{
    private const string Password = "A long Passw0rd for tests!";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Administrator_CreatesAnInvitedEmployee_SeenInListAndDetail()
    {
        var (admin, _) = await StaffAsync();
        var email = NewEmail();

        using var created = await SendAsync(HttpMethod.Post, "/api/v1/employees", admin, NewEmployee(email));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var body = (await created.Content.ReadFromJsonAsync<CreateEmployeeResponse>(Ct))!;
        body.InvitationSent.ShouldBeTrue();
        created.Headers.Location!.ToString().ShouldBe($"/api/v1/employees/{body.Id}");

        var detail = await DetailAsync(admin, body.Id);
        (detail.UserName, detail.Email, detail.CanSignIn, detail.IsActivated, detail.IsDefault).ShouldBe((email, email, true, false, false));
        (detail.FirstName, detail.LastName, detail.BirthDate).ShouldBe(("Paola", "Neri", new DateOnly(1985, 3, 4)));
        detail.Workload.AssignedClients.ShouldBe(0);
        detail.CreatedAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));

        var list = await ListAsync(admin, $"filter[email]={Uri.EscapeDataString(email)}");
        var row = list.Items.ShouldHaveSingleItem();
        (row.Id, row.UserName, row.CanSignIn, row.IsDefault, row.AssignedClients).ShouldBe((body.Id, email, true, false, 0));

        using var off = await SendAsync(HttpMethod.Post, "/api/v1/employees", admin, NewEmployee(NewEmail()) with { CanSignIn = false });
        (await off.Content.ReadFromJsonAsync<CreateEmployeeResponse>(Ct))!.InvitationSent.ShouldBeFalse();
        var inactive = await ListAsync(admin, $"filter[status]=inactive&filter[email]={Uri.EscapeDataString("@example.test")}&pageSize=100");
        inactive.Items.ShouldAllBe(item => !item.CanSignIn);
    }

    [Fact]
    public async Task InvalidAndDuplicateEmployees_AreRefused()
    {
        var (admin, _) = await StaffAsync();
        var email = NewEmail();

        using var invalid = await SendAsync(HttpMethod.Post, "/api/v1/employees", admin, new CreateEmployeeRequest("", "Neri", null, "nope", "12", "XYZ", true));
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await invalid.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("errorCode").GetString().ShouldBe($"AUX-{EventCodes.Directory.PersonInvalid}");
        problem.GetProperty("errors").EnumerateObject().Select(field => field.Name)
            .ShouldBe(["firstName", "email", "phone", "fiscalCode", "birthDate"], ignoreOrder: true);

        (await SendAsync(HttpMethod.Post, "/api/v1/employees", admin, NewEmployee(email))).StatusCode.ShouldBe(HttpStatusCode.Created);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, "/api/v1/employees", admin, NewEmployee(email.ToUpperInvariant())),
            HttpStatusCode.Conflict, EventCodes.Identity.UserNameTaken);

        using var invalidList = await SendAsync(HttpMethod.Get, "/api/v1/employees?filter[status]=gone&sort=age", admin);
        (await ErrorFieldsAsync(invalidList)).ShouldBe(["status", "sort"], ignoreOrder: true);
    }

    [Fact]
    public async Task Update_SignIn_Specializations_Administrator_AndCredentials()
    {
        var (admin, adminId) = await StaffAsync();
        var id = await CreateAsync(admin);

        var userName = "employee-" + Guid.NewGuid().ToString("N")[..8];
        using var updated = await SendAsync(HttpMethod.Put, $"/api/v1/employees/{id}", admin,
            new UpdateEmployeeRequest("Paola", "Bianchi", new DateOnly(1985, 3, 4), NewEmail(), "06 1234 5678", null, userName));
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync(Ct));
        var detail = (await updated.Content.ReadFromJsonAsync<EmployeeDetailResponse>(Ct))!;
        (detail.LastName, detail.UserName, detail.Phone).ShouldBe(("Bianchi", userName, "06 1234 5678"));

        using var disabled = await SendAsync(HttpMethod.Put, $"/api/v1/employees/{id}/sign-in", admin, new SetEmployeeSignInRequest(false));
        (await disabled.Content.ReadFromJsonAsync<EmployeeDetailResponse>(Ct))!.CanSignIn.ShouldBeFalse();
        (await SendAsync(HttpMethod.Put, $"/api/v1/employees/{id}/sign-in", admin, new SetEmployeeSignInRequest(true))).StatusCode.ShouldBe(HttpStatusCode.OK);

        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Put, $"/api/v1/employees/{id}/specializations", admin, new SetEmployeeSpecializationsRequest([Guid.NewGuid()])),
            HttpStatusCode.BadRequest, EventCodes.Directory.EmployeeSpecializationInvalid);
        using var cleared = await SendAsync(HttpMethod.Put, $"/api/v1/employees/{id}/specializations", admin, new SetEmployeeSpecializationsRequest([]));
        (await cleared.Content.ReadFromJsonAsync<EmployeeDetailResponse>(Ct))!.Specializations.ShouldBeEmpty();

        (await AdministratorsAsync(admin)).Select(item => item.UserId).ShouldContain(adminId);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Put, $"/api/v1/employees/{id}/administrator", admin, new SetEmployeeAdministratorRequest(id)),
            HttpStatusCode.BadRequest, EventCodes.Directory.AdministratorInvalid);
        using var reporting = await SendAsync(HttpMethod.Put, $"/api/v1/employees/{id}/administrator", admin, new SetEmployeeAdministratorRequest(adminId));
        (await reporting.Content.ReadFromJsonAsync<EmployeeDetailResponse>(Ct))!.Administrator!.UserId.ShouldBe(adminId);
        using var alone = await SendAsync(HttpMethod.Delete, $"/api/v1/employees/{id}/administrator", admin);
        (await alone.Content.ReadFromJsonAsync<EmployeeDetailResponse>(Ct))!.Administrator.ShouldBeNull();

        using var invited = await SendAsync(HttpMethod.Post, $"/api/v1/employees/{id}/invitation", admin);
        (await invited.Content.ReadFromJsonAsync<EmployeeInvitationResponse>(Ct))!.Sent.ShouldBeTrue();
        using var reset = await SendAsync(HttpMethod.Post, $"/api/v1/employees/{id}/password-reset", admin, new ResetEmployeePasswordRequest(false));
        reset.StatusCode.ShouldBe(HttpStatusCode.OK, await reset.Content.ReadAsStringAsync(Ct));
        reset.Headers.CacheControl!.NoStore.ShouldBeTrue();
        (await reset.Content.ReadFromJsonAsync<EmployeePasswordResetResponse>(Ct))!.TemporaryPassword.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task DefaultEmployee_ServesNewClients_AndReceivesTheClientsOfADeletedEmployee()
    {
        var (admin, _) = await StaffAsync();
        var fallback = await CreateAsync(admin);
        var leaving = await CreateAsync(admin);

        using var made = await SendAsync(HttpMethod.Put, $"/api/v1/employees/{fallback}/default", admin);
        made.StatusCode.ShouldBe(HttpStatusCode.OK, await made.Content.ReadAsStringAsync(Ct));
        (await made.Content.ReadFromJsonAsync<EmployeeDetailResponse>(Ct))!.IsDefault.ShouldBeTrue();

        // Q31: a client created by an Administrator without an employee goes to the default one.
        var client = await CreateClientAsync(admin, null);
        (await ClientEmployeeAsync(admin, client)).ShouldBe(fallback);

        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Put, $"/api/v1/employees/{fallback}/sign-in", admin, new SetEmployeeSignInRequest(false)),
            HttpStatusCode.Conflict, EventCodes.Directory.EmployeeIsDefault);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Delete, $"/api/v1/employees/{fallback}", admin),
            HttpStatusCode.Conflict, EventCodes.Directory.EmployeeIsDefault);

        // The clients in charge are the client list filtered by employee; deleting the employee hands them over.
        var handed = await CreateClientAsync(admin, leaving);
        using var inCharge = await SendAsync(HttpMethod.Get, $"/api/v1/clients?filter[employeeUserId]={leaving}", admin);
        (await inCharge.Content.ReadFromJsonAsync<PagedResponse<ClientListItemResponse>>(Ct))!.Items.Select(item => item.Id).ShouldBe([handed]);
        (await DetailAsync(admin, leaving)).Workload.AssignedClients.ShouldBe(1);

        (await SendAsync(HttpMethod.Delete, $"/api/v1/employees/{leaving}", admin)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Get, $"/api/v1/employees/{leaving}", admin)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ClientEmployeeAsync(admin, handed)).ShouldBe(fallback);

        // Another employee becomes the default: still exactly one.
        var next = await CreateAsync(admin);
        (await SendAsync(HttpMethod.Put, $"/api/v1/employees/{next}/default", admin)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await DetailAsync(admin, next)).IsDefault.ShouldBeTrue();
        (await DetailAsync(admin, fallback)).IsDefault.ShouldBeFalse();
    }

    [Fact]
    public async Task Employees_AreForAdministratorsOnly_AndStayInTheirTenant()
    {
        var (admin, _) = await StaffAsync();
        var id = await CreateAsync(admin);
        var (_, employeeName) = await factory.AddUserAsync([TenantRole.Employee], Password);
        var employee = await factory.SignInAsync(employeeName, Password, Ct);
        var (_, clientName) = await factory.AddUserAsync([TenantRole.Client], Password);
        var client = await factory.SignInAsync(clientName, Password, Ct);

        (await SendAsync(HttpMethod.Get, "/api/v1/employees", employee)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, "/api/v1/employees", employee, NewEmployee(NewEmail()))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, "/api/v1/employees", client)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Get, "/api/v1/employees", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // An Administrator of tenant B cannot read an employee of tenant A.
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/employees/{id}");
        request.Headers.Add(ApiFactory.TestTenantClaimHeader, ApiDatabase.TenantB);
        request.Headers.Add(ApiFactory.TestRolesHeader, "Administrator");
        request.Headers.Add("X-Tenant", ApiDatabase.TenantB);
        using var other = await http.SendAsync(request, Ct);
        other.StatusCode.ShouldBe(HttpStatusCode.NotFound, await other.Content.ReadAsStringAsync(Ct));
    }

    private static string NewEmail() => $"employee-{Guid.NewGuid():N}@example.test";

    private static CreateEmployeeRequest NewEmployee(string email) =>
        new("Paola", "Neri", new DateOnly(1985, 3, 4), email, "333 1234567", null, true);

    /// <summary>A valid fiscal code nobody else has.</summary>
    private static string NewFiscalCode()
    {
        static char Letter() => (char)('A' + Random.Shared.Next(26));
        static char Digit() => (char)('0' + Random.Shared.Next(10));
        return new string([Letter(), Letter(), Letter(), Letter(), Letter(), Letter(), Digit(), Digit(), 'A', Digit(), Digit(), Letter(), Digit(), Digit(), Digit(), Letter()]);
    }

    private async Task<(string Admin, Guid AdminId)> StaffAsync()
    {
        var (adminId, adminName) = await factory.AddUserAsync([TenantRole.Administrator], Password);
        return (await factory.SignInAsync(adminName, Password, Ct), adminId);
    }

    private async Task<Guid> CreateAsync(string token)
    {
        using var created = await SendAsync(HttpMethod.Post, "/api/v1/employees", token, NewEmployee(NewEmail()));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        return (await created.Content.ReadFromJsonAsync<CreateEmployeeResponse>(Ct))!.Id;
    }

    private async Task<Guid> CreateClientAsync(string token, Guid? employee)
    {
        var email = $"client-{Guid.NewGuid():N}@example.test";
        using var created = await SendAsync(HttpMethod.Post, "/api/v1/clients", token,
            new CreateClientRequest("Mario", "Rossi", new DateOnly(1980, 1, 1), email, null, NewFiscalCode(), null, employee));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        return (await created.Content.ReadFromJsonAsync<CreateClientResponse>(Ct))!.Id;
    }

    private async Task<Guid?> ClientEmployeeAsync(string token, Guid client)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/api/v1/clients/{client}", token);
        return (await response.Content.ReadFromJsonAsync<ClientDetailResponse>(Ct))!.Employee?.UserId;
    }

    private async Task<EmployeeDetailResponse> DetailAsync(string token, Guid id)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/api/v1/employees/{id}", token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<EmployeeDetailResponse>(Ct))!;
    }

    private async Task<PagedResponse<EmployeeListItemResponse>> ListAsync(string token, string query)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/v1/employees?" + query, token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<PagedResponse<EmployeeListItemResponse>>(Ct))!;
    }

    private async Task<EmployeeAdministratorResponse[]> AdministratorsAsync(string token)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/v1/employees/administrators", token);
        return (await response.Content.ReadFromJsonAsync<EmployeeAdministratorResponse[]>(Ct))!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, object? body = null)
    {
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        if (token is null)
        {
            request.Headers.Add("X-Tenant", ApiDatabase.TenantA);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await http.SendAsync(request, Ct);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString();

    private static async Task<string[]> ErrorFieldsAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").EnumerateObject().Select(field => field.Name).ToArray();

    private static async Task ShouldHaveCodeAsync(HttpResponseMessage response, HttpStatusCode status, int code)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync(Ct));
            (await ErrorCodeAsync(response)).ShouldBe($"AUX-{code}");
        }
    }

    public sealed class Factory : AuthEndpointsTests.Factory;
}
