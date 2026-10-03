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

namespace Auxilia.Api.IntegrationTests.Clients;

/// <summary>
/// B-01 over HTTP (F05): clients created by an Administrator or an Employee, lists, detail, edit, soft delete,
/// sign-in (Q60), assignment history, specializations, invitation and password reset, with the permissions of
/// each role and the tenant boundary.
/// </summary>
public sealed class ClientEndpointsTests(ClientEndpointsTests.Factory factory) : IClassFixture<ClientEndpointsTests.Factory>
{
    private const string Password = "A long Passw0rd for tests!";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Administrator_CreatesAnInvitedClient_ForAnEmployee_WhoSeesItAsTheirs()
    {
        var (admin, employee, employeeId) = await StaffAsync();
        var email = NewEmail();

        using var created = await SendAsync(HttpMethod.Post, "/api/v1/clients", admin, NewClient(email, employeeId));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var body = (await created.Content.ReadFromJsonAsync<CreateClientResponse>(Ct))!;
        body.InvitationSent.ShouldBeTrue();
        created.Headers.Location!.ToString().ShouldBe($"/api/v1/clients/{body.Id}");

        var detail = await DetailAsync(admin, body.Id);
        (detail.Account!.UserName, detail.Account.CanSignIn, detail.Account.IsActivated).ShouldBe((email, true, false));
        (detail.Employee!.UserId, detail.Status, detail.FiscalCode).ShouldBe((employeeId, "Inactive", detail.FiscalCode));
        detail.Assignments.ShouldHaveSingleItem().EndedAt.ShouldBeNull();

        var mine = await ListAsync(employee, "view=mine&pageSize=100");
        mine.Items.ShouldContain(item => item.Id == body.Id && item.Employee!.UserId == employeeId && item.CanSignIn);
        var byMail = await ListAsync(admin, $"filter[email]={Uri.EscapeDataString(email)}");
        byMail.Items.ShouldHaveSingleItem().Id.ShouldBe(body.Id);
    }

    [Fact]
    public async Task Employee_CreatesAClientForThemselves_ThatCannotSignInYet()
    {
        var (_, employee, employeeId) = await StaffAsync();

        using var created = await SendAsync(HttpMethod.Post, "/api/v1/clients", employee, NewClient(NewEmail(), null));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var body = (await created.Content.ReadFromJsonAsync<CreateClientResponse>(Ct))!;
        body.InvitationSent.ShouldBeFalse();

        var detail = await DetailAsync(employee, body.Id);
        (detail.Account!.CanSignIn, detail.Employee!.UserId).ShouldBe((false, employeeId));

        // Q60: the client has an employee in charge, so the employee may enable the sign-in.
        using var enabled = await SendAsync(HttpMethod.Put, $"/api/v1/clients/{body.Id}/sign-in", employee, new SetClientSignInRequest(true));
        enabled.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await enabled.Content.ReadFromJsonAsync<ClientDetailResponse>(Ct))!.Account!.CanSignIn.ShouldBeTrue();
    }

    [Fact]
    public async Task InvalidAndDuplicateClients_AreRefused_AndAFailedCreationLeavesNothing()
    {
        var (admin, _, _) = await StaffAsync();
        var fiscalCode = NewFiscalCode();
        var email = NewEmail();

        using var invalid = await SendAsync(HttpMethod.Post, "/api/v1/clients", admin,
            new CreateClientRequest("", "Rossi", null, "nope", "12", "XYZ", null, null));
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await invalid.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("errorCode").GetString().ShouldBe($"AUX-{EventCodes.Directory.PersonInvalid}");
        problem.GetProperty("errors").EnumerateObject().Select(field => field.Name)
            .ShouldBe(["firstName", "email", "phone", "fiscalCode", "birthDate"], ignoreOrder: true);

        (await SendAsync(HttpMethod.Post, "/api/v1/clients", admin, NewClient(email, null, fiscalCode))).StatusCode.ShouldBe(HttpStatusCode.Created);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, "/api/v1/clients", admin, NewClient(NewEmail(), null, fiscalCode.ToLowerInvariant())),
            HttpStatusCode.Conflict, EventCodes.Directory.FiscalCodeTaken);

        // The user name is taken: person and profile of the attempt are rolled back with it, so the fiscal code stays free.
        var otherCode = NewFiscalCode();
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, "/api/v1/clients", admin, NewClient(email.ToUpperInvariant(), null, otherCode)),
            HttpStatusCode.Conflict, EventCodes.Identity.UserNameTaken);
        (await SendAsync(HttpMethod.Post, "/api/v1/clients", admin, NewClient(NewEmail(), null, otherCode))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Update_AssignmentHistory_SignInRule_AndDelete()
    {
        var (admin, employee, employeeId) = await StaffAsync();
        var (otherEmployeeId, _) = await factory.AddUserAsync([TenantRole.Employee], Password);
        var assignable = (await AssignableAsync(admin)).Select(item => item.UserId).ToArray();
        assignable.ShouldContain(employeeId);
        assignable.ShouldContain(otherEmployeeId);
        var id = await CreateAsync(admin, null);

        // Other test classes may have set a default employee (Q31) on the shared tenant: nobody in charge for this check.
        (await SendAsync(HttpMethod.Delete, $"/api/v1/clients/{id}/employee", admin)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Put, $"/api/v1/clients/{id}/sign-in", admin, new SetClientSignInRequest(true)),
            HttpStatusCode.Conflict, EventCodes.Directory.ClientEmployeeRequired);

        (await SendAsync(HttpMethod.Put, $"/api/v1/clients/{id}/employee", admin, new AssignClientEmployeeRequest(employeeId))).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var reassigned = await SendAsync(HttpMethod.Put, $"/api/v1/clients/{id}/employee", admin, new AssignClientEmployeeRequest(otherEmployeeId));
        var history = (await reassigned.Content.ReadFromJsonAsync<ClientDetailResponse>(Ct))!.Assignments;
        history.Take(2).Select(item => (item.EmployeeUserId, item.EndedAt is null)).ShouldBe([(otherEmployeeId, true), (employeeId, false)]);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Put, $"/api/v1/clients/{id}/employee", admin, new AssignClientEmployeeRequest(Guid.NewGuid())),
            HttpStatusCode.BadRequest, EventCodes.Directory.EmployeeInvalid);

        var userName = "client-" + Guid.NewGuid().ToString("N")[..8];
        using var updated = await SendAsync(HttpMethod.Put, $"/api/v1/clients/{id}", employee,
            new UpdateClientRequest("Maria", "Verdi", new DateOnly(1985, 5, 5), NewEmail(), "06 1234 5678", NewFiscalCode(), userName, null));
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync(Ct));
        var detail = (await updated.Content.ReadFromJsonAsync<ClientDetailResponse>(Ct))!;
        (detail.FirstName, detail.LastName, detail.Account!.UserName).ShouldBe(("Maria", "Verdi", userName));

        // Q52: the user name stays unique.
        var takenBy = await CreateAsync(admin, null);
        var taken = await DetailAsync(admin, takenBy);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Put, $"/api/v1/clients/{id}", admin,
                new UpdateClientRequest("Maria", "Verdi", new DateOnly(1985, 5, 5), detail.Email!, null, detail.FiscalCode!, taken.Account!.UserName, null)),
            HttpStatusCode.Conflict, EventCodes.Identity.UserNameTaken);

        (await SendAsync(HttpMethod.Delete, $"/api/v1/clients/{id}", employee)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/clients/{id}", admin)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Get, $"/api/v1/clients/{id}", admin)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListAsync(admin, $"filter[userName]={userName}")).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Specializations_Invitation_AndPasswordReset()
    {
        var (admin, employee, _) = await StaffAsync();
        var id = await CreateAsync(admin, null);

        using var noSpecialization = await SendAsync(HttpMethod.Put, $"/api/v1/clients/{id}/specializations", employee, new SetClientSpecializationsRequest([Guid.NewGuid()]));
        await ShouldHaveCodeAsync(noSpecialization, HttpStatusCode.BadRequest, EventCodes.Directory.ClientSpecializationInvalid);
        using var offered = await SendAsync(HttpMethod.Get, "/api/v1/clients/specializations", employee);
        offered.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await offered.Content.ReadFromJsonAsync<ClientSpecializationResponse[]>(Ct)).ShouldNotBeNull();
        using var cleared = await SendAsync(HttpMethod.Put, $"/api/v1/clients/{id}/specializations", employee, new SetClientSpecializationsRequest([]));
        (await cleared.Content.ReadFromJsonAsync<ClientDetailResponse>(Ct))!.Specializations.ShouldBeEmpty();

        using var invited = await SendAsync(HttpMethod.Post, $"/api/v1/clients/{id}/invitation", employee);
        (await invited.Content.ReadFromJsonAsync<ClientInvitationResponse>(Ct))!.Sent.ShouldBeTrue();

        (await SendAsync(HttpMethod.Post, $"/api/v1/clients/{id}/password-reset", employee, new ResetClientPasswordRequest(false))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var reset = await SendAsync(HttpMethod.Post, $"/api/v1/clients/{id}/password-reset", admin, new ResetClientPasswordRequest(false));
        reset.StatusCode.ShouldBe(HttpStatusCode.OK, await reset.Content.ReadAsStringAsync(Ct));
        reset.Headers.CacheControl!.NoStore.ShouldBeTrue();
        (await reset.Content.ReadFromJsonAsync<ClientPasswordResetResponse>(Ct))!.TemporaryPassword.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Clients_AreForStaffOnly_AndStayInTheirTenant()
    {
        var (admin, _, _) = await StaffAsync();
        var id = await CreateAsync(admin, null);
        var (_, clientName) = await factory.AddUserAsync([TenantRole.Client], Password);
        var client = await factory.SignInAsync(clientName, Password, Ct);

        // The Client role does not see the module: not found, never forbidden (no hint that it exists).
        (await SendAsync(HttpMethod.Get, "/api/v1/clients", client)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Get, "/api/v1/clients", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // An Administrator of tenant B cannot read a client of tenant A.
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/clients/{id}");
        request.Headers.Add(ApiFactory.TestTenantClaimHeader, ApiDatabase.TenantB);
        request.Headers.Add(ApiFactory.TestRolesHeader, "Administrator");
        request.Headers.Add("X-Tenant", ApiDatabase.TenantB);
        using var other = await http.SendAsync(request, Ct);
        other.StatusCode.ShouldBe(HttpStatusCode.NotFound, await other.Content.ReadAsStringAsync(Ct));

        using var invalid = await SendAsync(HttpMethod.Get, "/api/v1/clients?view=team&sort=age", admin);
        (await ErrorFieldsAsync(invalid)).ShouldBe(["view", "sort"], ignoreOrder: true);
    }

    private static string NewEmail() => $"client-{Guid.NewGuid():N}@example.test";

    /// <summary>A valid fiscal code nobody else has.</summary>
    private static string NewFiscalCode()
    {
        static char Letter() => (char)('A' + Random.Shared.Next(26));
        static char Digit() => (char)('0' + Random.Shared.Next(10));
        return new string([Letter(), Letter(), Letter(), Letter(), Letter(), Letter(), Digit(), Digit(), 'A', Digit(), Digit(), Letter(), Digit(), Digit(), Digit(), Letter()]);
    }

    private static CreateClientRequest NewClient(string email, Guid? employee, string? fiscalCode = null) =>
        new("Mario", "Rossi", new DateOnly(1980, 1, 1), email, "333 1234567", fiscalCode ?? NewFiscalCode(), null, employee);

    private async Task<(string Admin, string Employee, Guid EmployeeId)> StaffAsync()
    {
        var (_, adminName) = await factory.AddUserAsync([TenantRole.Administrator], Password);
        var (employeeId, employeeName) = await factory.AddUserAsync([TenantRole.Employee], Password);
        return (await factory.SignInAsync(adminName, Password, Ct), await factory.SignInAsync(employeeName, Password, Ct), employeeId);
    }

    private async Task<Guid> CreateAsync(string token, Guid? employee)
    {
        using var created = await SendAsync(HttpMethod.Post, "/api/v1/clients", token, NewClient(NewEmail(), employee));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        return (await created.Content.ReadFromJsonAsync<CreateClientResponse>(Ct))!.Id;
    }

    private async Task<ClientDetailResponse> DetailAsync(string token, Guid id)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/api/v1/clients/{id}", token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ClientDetailResponse>(Ct))!;
    }

    private async Task<PagedResponse<ClientListItemResponse>> ListAsync(string token, string query)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/v1/clients?" + query, token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<PagedResponse<ClientListItemResponse>>(Ct))!;
    }

    private async Task<ClientEmployeeResponse[]> AssignableAsync(string token)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/v1/clients/assignable-employees", token);
        return (await response.Content.ReadFromJsonAsync<ClientEmployeeResponse[]>(Ct))!;
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
