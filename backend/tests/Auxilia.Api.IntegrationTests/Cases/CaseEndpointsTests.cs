using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Identity;
using Auxilia.Contracts.Cases;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Directory;
using Auxilia.Diagnostics;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.Persistence.Tenant;
using Auxilia.SharedKernel.Tenancy;

using Npgsql;

namespace Auxilia.Api.IntegrationTests.Cases;

/// <summary>
/// B-08 over HTTP (F09, F10, Q02–Q04, D-04, D-07): open, workflow, payments, completion, client status, private cases
/// for employees, soft delete keeping the timeline, services with cases kept. Clients seeing their own cases is covered
/// by the Application tests: in the test tenants the module is visible to staff only.
/// </summary>
public sealed class CaseEndpointsTests(CaseEndpointsTests.Factory factory) : IClassFixture<CaseEndpointsTests.Factory>
{
    private const string Password = "A long Passw0rd for tests!";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Administrator_RunsTheWholeWorkflow_AndTheClientStatusFollows()
    {
        var admin = await SignInAsync(TenantRole.Administrator);
        var (clientId, _) = await Factory.AddClientAsync();
        var serviceId = await ServiceAsync(admin, null, 150m);

        var opened = await OpenAsync(admin, new OpenCaseRequest(clientId, serviceId, new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), null, null, null));
        opened.Number.ShouldMatch(@"^\d{4}-\d{5}$");
        (opened.Status, opened.Price, opened.AmountPaid, opened.StartedOn, opened.DueOn, opened.ExpiresOn, opened.CanManage)
            .ShouldBe(("Inserted", 150m, 0m, new DateOnly(2026, 9, 1), (DateOnly?)new DateOnly(2026, 12, 31), (DateOnly?)null, true));
        (await ClientStatusAsync(admin, clientId)).ShouldBe("Active");

        (await ChangeAsync(admin, opened.Id, "payments", new AddCasePaymentRequest(50m, null, "acconto"))).AmountPaid.ShouldBe(50m);
        (await ChangeAsync(admin, opened.Id, "advance", new ChangeCaseStatusRequest("documents arrived"))).Status.ShouldBe("InProgress");
        (await ChangeAsync(admin, opened.Id, "back", null)).Status.ShouldBe("Inserted");
        await ChangeAsync(admin, opened.Id, "advance", null);
        (await ChangeAsync(admin, opened.Id, "advance", null)).Status.ShouldBe("Sent");
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, $"/api/v1/cases/{opened.Id}/advance", admin), HttpStatusCode.Conflict,
            EventCodes.Cases.CaseCompletionRequired);

        var completed = await ChangeAsync(admin, opened.Id, "complete", new CompleteCaseRequest(null, true, "closed"));
        (completed.Status, completed.IsRejected, completed.AmountPaid, completed.ExpiresOn is not null, completed.CompletedAt is not null)
            .ShouldBe(("Completed", true, 150m, true, true));
        completed.History.Select(change => change.ToStatus).ShouldBe(["Inserted", "InProgress", "Inserted", "InProgress", "Sent", "Completed"]);
        completed.Payments.Select(payment => payment.Amount).ShouldBe([50m, 100m]);
        (await ClientStatusAsync(admin, clientId)).ShouldBe("Inactive");

        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, $"/api/v1/cases/{opened.Id}/back", admin), HttpStatusCode.Conflict, EventCodes.Cases.CaseIsCompleted);

        // Q28: a service with cases is deactivated, not deleted.
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Delete, $"/api/v1/services/{serviceId}", admin), HttpStatusCode.Conflict, EventCodes.Cases.ServiceInUse);
    }

    [Fact]
    public async Task InvalidRequests_AreFieldErrors()
    {
        var admin = await SignInAsync(TenantRole.Administrator);

        using var invalid = await SendAsync(HttpMethod.Post, "/api/v1/cases", admin,
            new OpenCaseRequest(Guid.NewGuid(), Guid.NewGuid(), null, null, Guid.NewGuid(), null, null));
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await invalid.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("errorCode").GetString().ShouldBe($"AUX-{EventCodes.Cases.CaseInvalid}");
        problem.GetProperty("errors").EnumerateObject().Select(field => field.Name).ShouldBe(["clientId", "serviceId", "specializationId"], ignoreOrder: true);

        var (clientId, _) = await Factory.AddClientAsync();
        var opened = await OpenAsync(admin, new OpenCaseRequest(clientId, await ServiceAsync(admin, null, 10m), null, null, null, null, null));
        using var badPayment = await SendAsync(HttpMethod.Post, $"/api/v1/cases/{opened.Id}/payments", admin, new AddCasePaymentRequest(0m, new DateOnly(2999, 1, 1), null));
        badPayment.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await badPayment.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").EnumerateObject().Select(field => field.Name)
            .ShouldBe(["amount", "paidOn"], ignoreOrder: true);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Get, $"/api/v1/cases/{Guid.NewGuid()}", admin), HttpStatusCode.NotFound, EventCodes.Cases.CaseNotFound);
    }

    [Fact]
    public async Task PrivateCases_FollowD04_ForEmployees()
    {
        var admin = await SignInAsync(TenantRole.Administrator);
        var (employeeId, employeeName) = await factory.AddUserAsync([TenantRole.Employee], Password);
        var employee = await factory.SignInAsync(employeeName, Password, Ct);
        var held = await Factory.AddSpecializationAsync(isPrivate: true, member: employeeId);
        var notHeld = await Factory.AddSpecializationAsync(isPrivate: true, member: null);
        var (clientId, _) = await Factory.AddClientAsync();

        var heldCase = await OpenAsync(admin, new OpenCaseRequest(clientId, await ServiceAsync(admin, held, 10m), null, null, null, null, null));
        var hiddenCase = await OpenAsync(admin, new OpenCaseRequest(clientId, await ServiceAsync(admin, notHeld, 10m), null, null, null, null, null));

        (await DetailAsync(employee, heldCase.Id)).CanManage.ShouldBeTrue();
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Get, $"/api/v1/cases/{hiddenCase.Id}", employee), HttpStatusCode.NotFound, EventCodes.Cases.CaseNotFound);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, $"/api/v1/cases/{hiddenCase.Id}/advance", employee), HttpStatusCode.NotFound, EventCodes.Cases.CaseNotFound);

        // F10: an employee opens cases only for specializations held (or none).
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, "/api/v1/cases", employee,
            new OpenCaseRequest(clientId, await ServiceAsync(admin, notHeld, 10m), null, null, null, null, null)), HttpStatusCode.Forbidden, EventCodes.Identity.PermissionDenied);
        (await DetailAsync(admin, hiddenCase.Id)).CanManage.ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_IsSoft_KeepsTheTimeline_AndEmployeesCannotDeleteCompletedCases()
    {
        var admin = await SignInAsync(TenantRole.Administrator);
        var employee = await SignInAsync(TenantRole.Employee);
        var (clientId, _) = await Factory.AddClientAsync();
        var serviceId = await ServiceAsync(admin, null, 0m);
        var deleted = await OpenAsync(admin, new OpenCaseRequest(clientId, serviceId, null, null, null, null, null));
        var completed = await OpenAsync(admin, new OpenCaseRequest(clientId, serviceId, null, null, null, null, null));

        (await SendAsync(HttpMethod.Delete, $"/api/v1/cases/{deleted.Id}", employee)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Get, $"/api/v1/cases/{deleted.Id}", admin)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Factory.HistoryRowsAsync(deleted.Id)).ShouldBe(1);

        await ChangeAsync(admin, completed.Id, "advance", null);
        await ChangeAsync(admin, completed.Id, "advance", null);
        await ChangeAsync(admin, completed.Id, "complete", new CompleteCaseRequest(0m, false, null));
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Delete, $"/api/v1/cases/{completed.Id}", employee), HttpStatusCode.Forbidden, EventCodes.Identity.PermissionDenied);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/cases/{completed.Id}", admin)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ClientStatusAsync(admin, clientId)).ShouldBe("Inactive");

        // Only deleted cases left: the service can go.
        (await SendAsync(HttpMethod.Delete, $"/api/v1/services/{serviceId}", admin)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Lists_ApplyF10InTheQuery_WithTheEmployeeToggles()
    {
        var admin = await SignInAsync(TenantRole.Administrator);
        var (employeeId, employeeName) = await factory.AddUserAsync([TenantRole.Employee], Password);
        var employee = await factory.SignInAsync(employeeName, Password, Ct);
        var publicNotHeld = await Factory.AddSpecializationAsync(isPrivate: false, member: null);
        var privateHeld = await Factory.AddSpecializationAsync(isPrivate: true, member: employeeId);
        var privateNotHeld = await Factory.AddSpecializationAsync(isPrivate: true, member: null);
        var (clientId, _) = await Factory.AddClientAsync();
        var plainService = await ServiceAsync(admin, null, 10m);

        async Task<Guid> CaseAsync(Guid? specialization, DateOnly startedOn) =>
            (await OpenAsync(admin, new OpenCaseRequest(clientId, specialization is null ? plainService : await ServiceAsync(admin, specialization, 20m), startedOn, null, null, null, null))).Id;

        var none = await CaseAsync(null, new DateOnly(2026, 1, 1));
        var publicCase = await CaseAsync(publicNotHeld, new DateOnly(2026, 2, 1));
        var heldCase = await CaseAsync(privateHeld, new DateOnly(2026, 3, 1));
        var hiddenCase = await CaseAsync(privateNotHeld, new DateOnly(2026, 4, 1));
        var completed = await CaseAsync(null, new DateOnly(2026, 5, 1));
        await ChangeAsync(admin, completed, "advance", null);
        await ChangeAsync(admin, completed, "advance", null);
        await ChangeAsync(admin, completed, "complete", new CompleteCaseRequest(10m, false, null));

        var mine = $"filter[clientId]={clientId}&pageSize=100";
        (await CaseIdsAsync(admin, mine)).ShouldBe([completed, hiddenCase, heldCase, publicCase, none]);

        // Employee defaults: show all off (no specialization or held) and show completed off.
        (await CaseIdsAsync(employee, mine)).ShouldBe([heldCase, none]);
        (await CaseIdsAsync(employee, mine + "&showAll=true")).ShouldBe([heldCase, publicCase, none]);
        (await CaseIdsAsync(employee, mine + "&showAll=true&showCompleted=true&sort=startedOn")).ShouldBe([none, publicCase, heldCase, completed]);

        // The detail follows the same rule; visible but not held is read-only.
        (await DetailAsync(employee, publicCase)).CanManage.ShouldBeFalse();
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, $"/api/v1/cases/{publicCase}/advance", employee), HttpStatusCode.Forbidden, EventCodes.Identity.PermissionDenied);
        (await DetailAsync(employee, heldCase)).CanManage.ShouldBeTrue();
        (await SendAsync(HttpMethod.Get, $"/api/v1/cases/{hiddenCase}", employee)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Filters and sorts.
        (await CaseIdsAsync(admin, $"filter[serviceId]={plainService}&pageSize=100")).ShouldBe([completed, none]);
        (await CaseIdsAsync(admin, mine + "&filter[status]=Completed")).ShouldBe([completed]);
        var page = await ListAsync(admin, $"filter[clientId]={clientId}&sort=-amountPaid&pageSize=1");
        (page.TotalCount, page.Items.ShouldHaveSingleItem().Id, page.Items[0].AmountPaid, page.Items[0].Validity).ShouldBe((5, completed, 10m, "Active"));
        (await SendAsync(HttpMethod.Get, "/api/v1/cases?sort=age&filter[status]=Gone", admin)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task<PagedResponse<CaseListItemResponse>> ListAsync(string token, string query)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/v1/cases?" + query, token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<PagedResponse<CaseListItemResponse>>(Ct))!;
    }

    private async Task<Guid[]> CaseIdsAsync(string token, string query) => (await ListAsync(token, query)).Items.Select(item => item.Id).ToArray();

    private async Task<string> SignInAsync(TenantRole role)
    {
        var (_, userName) = await factory.AddUserAsync([role], Password);
        return await factory.SignInAsync(userName, Password, Ct);
    }

    private async Task<Guid> ServiceAsync(string token, Guid? specializationId, decimal price)
    {
        using var created = await SendAsync(HttpMethod.Post, "/api/v1/services", token,
            new CreateServiceRequest("Case service " + Guid.NewGuid().ToString("N")[..8], null, price, 30, null, specializationId));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        return (await created.Content.ReadFromJsonAsync<ServiceResponse>(Ct))!.Id;
    }

    private async Task<CaseResponse> OpenAsync(string token, OpenCaseRequest request)
    {
        using var created = await SendAsync(HttpMethod.Post, "/api/v1/cases", token, request);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        return (await created.Content.ReadFromJsonAsync<CaseResponse>(Ct))!;
    }

    private async Task<CaseResponse> DetailAsync(string token, Guid id)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/api/v1/cases/{id}", token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<CaseResponse>(Ct))!;
    }

    private async Task<CaseResponse> ChangeAsync(string token, Guid id, string action, object? body)
    {
        using var response = await SendAsync(HttpMethod.Post, $"/api/v1/cases/{id}/{action}", token, body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<CaseResponse>(Ct))!;
    }

    private async Task<string> ClientStatusAsync(string token, Guid clientId)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/api/v1/clients/{clientId}", token);
        return (await response.Content.ReadFromJsonAsync<ClientDetailResponse>(Ct))!.Status;
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
        private static TenantDbContext Tenant(NpgsqlDataSource dataSource) => new(TenantDbContextOptions.Create(dataSource));

        private static NpgsqlDataSource DataSource() =>
            NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(ApiDatabase.Instance.CatalogConnectionString) { Database = "tenant_a" }.ConnectionString);

        /// <summary>A client of tenant A (person, profile, account with the Client role).</summary>
        public static async Task<(Guid ClientId, string UserName)> AddClientAsync()
        {
            await using var dataSource = DataSource();
            await using var db = Tenant(dataSource);
            var person = new Person(Guid.CreateVersion7(), "Mario", "Rossi", null);
            db.Set<Person>().Add(person);
            db.Set<ClientProfile>().Add(ClientProfile.Create(person.Id, null, DateTimeOffset.UtcNow));
            var userName = "client-" + Guid.NewGuid().ToString("N")[..10];
            db.Set<User>().Add(User.Create(Guid.CreateVersion7(), person.Id, userName, userName + "@example.test", "it", [TenantRole.Client], isActive: true).Value);
            await db.SaveChangesAsync();
            return (person.Id, userName);
        }

        /// <summary>An active Employee specialization of tenant A, held by <paramref name="member"/> when given.</summary>
        public static async Task<Guid> AddSpecializationAsync(bool isPrivate, Guid? member)
        {
            await using var dataSource = DataSource();
            await using var db = Tenant(dataSource);
            var specialization = Specialization.Create(
                Guid.CreateVersion7(), TenantRole.Employee, new SpecializationSpec("Spec " + Guid.NewGuid().ToString("N")[..8], null, null, null, isPrivate)).Value;
            if (member is { } userId)
            {
                specialization.AddMembers([userId], DateTimeOffset.UtcNow);
            }

            db.Set<Specialization>().Add(specialization);
            await db.SaveChangesAsync();
            return specialization.Id;
        }

        /// <summary>Rows of the case timeline, deleted case included.</summary>
        public static async Task<int> HistoryRowsAsync(Guid caseId)
        {
            await using var dataSource = DataSource();
            await using var command = dataSource.CreateCommand("SELECT count(*) FROM cases.case_status_history WHERE case_id = $1");
            command.Parameters.AddWithValue(caseId);
            return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
