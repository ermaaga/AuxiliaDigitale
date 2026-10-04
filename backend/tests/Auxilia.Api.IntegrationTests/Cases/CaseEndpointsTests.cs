using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Identity;
using Auxilia.Contracts.Cases;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Directory;
using Auxilia.Contracts.Engagement;
using Auxilia.Contracts.Marketing;
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
    public async Task ExpiryReminder_EmailsTheClient_AndNeedsAnEndDate()
    {
        var admin = await SignInAsync(TenantRole.Administrator);
        var (clientId, userName) = await Factory.AddClientAsync();
        var serviceId = await ServiceAsync(admin, null, 90m);
        var dated = await OpenAsync(admin, new OpenCaseRequest(clientId, serviceId, null, new DateOnly(2026, 12, 31), null, null, null));
        var undated = await OpenAsync(admin, new OpenCaseRequest(clientId, serviceId, null, null, null, null, null));

        (await SendAsync(HttpMethod.Post, $"/api/v1/cases/{dated.Id}/expiry-reminder", admin)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var message = factory.Messages.Single(item => item.RelatedEntityId == dated.Id);
        (message.Recipient, message.TemplateCode, message.Language, message.Model["endDate"], message.Model["name"])
            .ShouldBe((userName + "@example.test", "case-expiry-reminder", "it", (object?)"31/12/2026", (object?)"Mario Rossi"));

        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, $"/api/v1/cases/{undated.Id}/expiry-reminder", admin), HttpStatusCode.Conflict,
            EventCodes.Cases.CaseHasNoEndDate);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, $"/api/v1/cases/{Guid.CreateVersion7()}/expiry-reminder", admin), HttpStatusCode.NotFound,
            EventCodes.Cases.CaseNotFound);
    }

    [Fact]
    public async Task Checklist_TasksAndTimeline_FollowTheCase()
    {
        var (adminId, adminName) = await factory.AddUserAsync([TenantRole.Administrator], Password);
        var admin = await factory.SignInAsync(adminName, Password, Ct);
        var (clientId, _) = await Factory.AddClientAsync();
        var serviceId = await ServiceAsync(admin, null, 120m);

        using var saved = await SendAsync(HttpMethod.Put, $"/api/v1/services/{serviceId}/checklist", admin,
            new SaveServiceChecklistRequest([new(null, "Documento d'identità", null, true), new(null, "CU", null, false)]));
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Ct));
        var items = (await saved.Content.ReadFromJsonAsync<ServiceChecklistItemResponse[]>(Ct))!;
        await ShouldHaveCodeAsync(
            await SendAsync(HttpMethod.Put, $"/api/v1/services/{serviceId}/checklist", admin, new SaveServiceChecklistRequest([new(null, "CU", null, true), new(null, "cu", null, true)])),
            HttpStatusCode.BadRequest, EventCodes.Cases.ServiceChecklistInvalid);

        var opened = await OpenAsync(admin, new OpenCaseRequest(clientId, serviceId, null, null, null, null, null));
        opened.Checklist.Select(item => (item.Name, item.Required, item.CheckedAt is null)).ShouldBe([("Documento d'identità", true, true), ("CU", false, true)]);
        using var ticked = await SendAsync(HttpMethod.Put, $"/api/v1/cases/{opened.Id}/checklist/{items[0].Id}", admin);
        ticked.StatusCode.ShouldBe(HttpStatusCode.OK, await ticked.Content.ReadAsStringAsync(Ct));
        (await ticked.Content.ReadFromJsonAsync<CaseResponse>(Ct))!.Checklist[0].CheckedBy!.UserId.ShouldBe(adminId);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Put, $"/api/v1/cases/{opened.Id}/checklist/{Guid.CreateVersion7()}", admin),
            HttpStatusCode.NotFound, EventCodes.Cases.CaseChecklistItemNotFound);

        using var task = await SendAsync(HttpMethod.Post, "/api/v1/tasks", admin,
            new SaveTaskRequest("Ask for the CU", null, new DateOnly(2020, 1, 1), adminId, clientId, opened.Id));
        task.StatusCode.ShouldBe(HttpStatusCode.Created, await task.Content.ReadAsStringAsync(Ct));
        var created = (await task.Content.ReadFromJsonAsync<TaskResponse>(Ct))!;
        (created.IsOverdue, created.Case!.Number, created.Client!.Id).ShouldBe((true, opened.Number, clientId));
        using var due = await SendAsync(HttpMethod.Get, $"/api/v1/tasks?filter[due]=today&filter[clientId]={clientId}", admin);
        (await due.Content.ReadFromJsonAsync<PagedResponse<TaskResponse>>(Ct))!.Items.Select(item => item.Id).ShouldBe([created.Id]);
        using var done = await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{created.Id}/complete", admin);
        (await done.Content.ReadFromJsonAsync<TaskResponse>(Ct))!.Status.ShouldBe("Done");

        (await SendAsync(HttpMethod.Post, $"/api/v1/clients/{clientId}/activities", admin, new AddActivityRequest("Call", "Asked for the CU", null)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        using var timeline = await SendAsync(HttpMethod.Get, $"/api/v1/clients/{clientId}/timeline", admin);
        timeline.StatusCode.ShouldBe(HttpStatusCode.OK, await timeline.Content.ReadAsStringAsync(Ct));
        var entries = (await timeline.Content.ReadFromJsonAsync<TimelineEntryResponse[]>(Ct))!;
        entries.Select(entry => entry.Kind).ShouldBe(["activity", "task.completed", "task.created", "case.status"]);
        entries[^1].TitleKey.ShouldBe("app.timeline.case.opened");

        var employee = await SignInAsync(TenantRole.Employee);
        (await SendAsync(HttpMethod.Get, $"/api/v1/tasks/{created.Id}", employee)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ConsentsAndTags_AreKeptPerClient()
    {
        var admin = await SignInAsync(TenantRole.Administrator);
        var (clientId, _) = await Factory.AddClientAsync();
        var (otherId, _) = await Factory.AddClientAsync();
        var name = "Tag " + Guid.NewGuid().ToString("N")[..8];

        using var created = await SendAsync(HttpMethod.Post, "/api/v1/tags", admin, new SaveTagRequest(name, "#72fa29"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var tagId = (await created.Content.ReadFromJsonAsync<CreateTagResponse>(Ct))!.Id;
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, "/api/v1/tags", admin, new SaveTagRequest(name.ToUpperInvariant(), null)),
            HttpStatusCode.BadRequest, EventCodes.Directory.TagInvalid);

        using var set = await SendAsync(HttpMethod.Put, $"/api/v1/clients/{clientId}/tags", admin, new SetClientTagsRequest([tagId]));
        (await set.Content.ReadFromJsonAsync<ClientTagResponse[]>(Ct))!.ShouldHaveSingleItem().Color.ShouldBe("#72FA29");
        using var bulk = await SendAsync(HttpMethod.Post, "/api/v1/clients/tags", admin, new BulkClientTagsRequest([clientId, otherId], [tagId], null));
        (await bulk.Content.ReadFromJsonAsync<BulkClientTagsResponse>(Ct))!.Changed.ShouldBe(1);
        using var tagged = await SendAsync(HttpMethod.Get, $"/api/v1/clients?filter[tagId]={tagId}", admin);
        (await tagged.Content.ReadFromJsonAsync<PagedResponse<ClientListItemResponse>>(Ct))!.Items.Select(item => item.Id).ShouldBe([clientId, otherId], ignoreOrder: true);

        (await SendAsync(HttpMethod.Post, $"/api/v1/clients/{clientId}/consents", admin, new RecordConsentRequest("Marketing", "Email", true, "2026-01", null)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        using var revoked = await SendAsync(HttpMethod.Post, $"/api/v1/clients/{clientId}/consents", admin, new RecordConsentRequest("Marketing", "Email", false, null, "asked"));
        var consents = (await revoked.Content.ReadFromJsonAsync<ClientConsentsResponse>(Ct))!;
        consents.Current.Single(state => state.Purpose == "Marketing" && state.Channel == "Email").ShouldSatisfyAllConditions(
            state => state.Granted.ShouldBeFalse(), state => state.Source.ShouldBe("Staff"));
        consents.History.Select(change => change.Granted).ShouldBe([false, true]);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, $"/api/v1/clients/{clientId}/consents", admin, new RecordConsentRequest("Spam", "Email", true, null, null)),
            HttpStatusCode.BadRequest, EventCodes.Directory.ConsentInvalid);

        var employee = await SignInAsync(TenantRole.Employee);
        (await SendAsync(HttpMethod.Post, "/api/v1/tags", employee, new SaveTagRequest("Nope", null))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/tags/{tagId}", admin)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var after = await SendAsync(HttpMethod.Get, $"/api/v1/clients/{clientId}/tags", admin);
        (await after.Content.ReadFromJsonAsync<ClientTagResponse[]>(Ct))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Segments_TranslateEveryConditionToSql_AndListsKeepTheirMembers()
    {
        var admin = await SignInAsync(TenantRole.Administrator);
        var (first, _) = await Factory.AddClientAsync();
        var (second, _) = await Factory.AddClientAsync();
        var serviceId = await ServiceAsync(admin, null, 50m);
        await OpenAsync(admin, new OpenCaseRequest(first, serviceId, null, null, null, null, null));
        using var tag = await SendAsync(HttpMethod.Post, "/api/v1/tags", admin, new SaveTagRequest("Segment " + Guid.NewGuid().ToString("N")[..8], null));
        var tagId = (await tag.Content.ReadFromJsonAsync<CreateTagResponse>(Ct))!.Id;
        await SendAsync(HttpMethod.Put, $"/api/v1/clients/{second}/tags", admin, new SetClientTagsRequest([tagId]));

        static SegmentConditionRequest Condition(string field, string op, object? value, string? key = null) =>
            new(field, op, value is null ? null : JsonSerializer.SerializeToElement(value), key);

        // Every field once, to run its SQL; the count itself depends on the shared database.
        using var everything = await SendAsync(HttpMethod.Post, "/api/v1/marketing/segments/preview", admin, new SegmentRuleRequest("any",
        [
            Condition("status", "isNot", "Active"), Condition("employee", "none", null), Condition("tag", "hasNot", tagId.ToString()),
            Condition("specialization", "has", Guid.NewGuid().ToString()), Condition("service", "has", serviceId.ToString()), Condition("caseStatus", "has", "Inserted"),
            Condition("age", "atLeast", 18), Condition("age", "atMost", 99), Condition("createdOn", "onOrAfter", "2020-01-01"), Condition("createdOn", "onOrBefore", "2100-01-01"),
            Condition("customField", "is", true, "caf"),
        ], [new SegmentGroupRequest("all", [Condition("employee", "isNot", Guid.NewGuid().ToString())])]));
        everything.StatusCode.ShouldBe(HttpStatusCode.OK, await everything.Content.ReadAsStringAsync(Ct));

        using var preview = await SendAsync(HttpMethod.Post, "/api/v1/marketing/segments/preview", admin, new SegmentRuleRequest("any",
            [Condition("tag", "has", tagId.ToString()), Condition("service", "has", serviceId.ToString())], null));
        var selected = (await preview.Content.ReadFromJsonAsync<SegmentPreviewResponse>(Ct))!;
        selected.Count.ShouldBe(2);
        selected.Sample.Select(member => member.Id).ShouldBe([first, second], ignoreOrder: true);

        using var created = await SendAsync(HttpMethod.Post, "/api/v1/marketing/segments", admin,
            new SaveSegmentRequest("Tagged " + Guid.NewGuid().ToString("N")[..8], null, new SegmentRuleRequest("all", [Condition("tag", "has", tagId.ToString())], null)));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var segmentId = (await created.Content.ReadFromJsonAsync<CreatedAudienceResponse>(Ct))!.Id;
        using var segment = await SendAsync(HttpMethod.Get, $"/api/v1/marketing/segments/{segmentId}", admin);
        (await segment.Content.ReadFromJsonAsync<SegmentResponse>(Ct))!.MemberCount.ShouldBe(1);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, "/api/v1/marketing/segments/preview", admin, new SegmentRuleRequest("all", [Condition("age", "atLeast", "old")], null)),
            HttpStatusCode.BadRequest, EventCodes.Marketing.SegmentInvalid);

        using var list = await SendAsync(HttpMethod.Post, "/api/v1/marketing/lists", admin, new SaveStaticListRequest("List " + Guid.NewGuid().ToString("N")[..8], null));
        var listId = (await list.Content.ReadFromJsonAsync<CreatedAudienceResponse>(Ct))!.Id;
        using var added = await SendAsync(HttpMethod.Post, $"/api/v1/marketing/lists/{listId}/members", admin, new ListMembersRequest([first, second, Guid.NewGuid()]));
        (await added.Content.ReadFromJsonAsync<ListMembersChangedResponse>(Ct))!.Changed.ShouldBe(2);
        await SendAsync(HttpMethod.Post, $"/api/v1/marketing/lists/{listId}/members/remove", admin, new ListMembersRequest([first]));
        using var members = await SendAsync(HttpMethod.Get, $"/api/v1/marketing/lists/{listId}/members", admin);
        (await members.Content.ReadFromJsonAsync<PagedResponse<AudienceMemberResponse>>(Ct))!.Items.Select(member => member.Id).ShouldBe([second]);

        // An employee sees only the clients in their charge (none of these).
        var employee = await SignInAsync(TenantRole.Employee);
        using var theirs = await SendAsync(HttpMethod.Get, $"/api/v1/marketing/lists/{listId}", employee);
        (await theirs.Content.ReadFromJsonAsync<StaticListResponse>(Ct))!.MemberCount.ShouldBe(0);
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
