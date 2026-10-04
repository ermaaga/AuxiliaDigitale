using System.Text.Json;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Cases;
using Auxilia.Application.Cases;
using Auxilia.Application.Configuration.Public;
using Auxilia.Application.Directory.Public;
using Auxilia.Application.Identity.Public;
using Auxilia.Application.Messaging.Public;
using Auxilia.Application.Tests.Identity;
using Auxilia.Application.Tests.Platform;
using Auxilia.Contracts.Cases;
using Auxilia.Diagnostics;
using Auxilia.Domain.Cases;
using Auxilia.Domain.Messaging;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using NSubstitute;

using Case = Auxilia.Domain.Cases.Case;

namespace Auxilia.Application.Tests.Cases;

public sealed class CaseManagerTests : IAsyncDisposable
{
    private static readonly Guid Admin = Guid.CreateVersion7();
    private static readonly Guid Employee = Guid.CreateVersion7();
    private static readonly Guid ClientUser = Guid.CreateVersion7();
    private static readonly Guid Client = Guid.CreateVersion7();
    private static readonly Guid OtherClient = Guid.CreateVersion7();
    private static readonly Guid Private = Guid.CreateVersion7();
    private static readonly Guid Public = Guid.CreateVersion7();

    private readonly InMemoryCases data = new();
    private readonly IClientDirectory clients = Substitute.For<IClientDirectory>();
    private readonly ICustomFieldValidator customFields = Substitute.For<ICustomFieldValidator>();
    private readonly IAccessGuard guard = Substitute.For<IAccessGuard>();
    private readonly IPermissionAccess permissions = Substitute.For<IPermissionAccess>();
    private readonly ICurrentUser caller = Substitute.For<ICurrentUser>();
    private readonly IUserAccounts accounts = Substitute.For<IUserAccounts>();
    private readonly IMessageDispatcher messages = Substitute.For<IMessageDispatcher>();
    private readonly ManualTimeProvider clock = new();
    private readonly Dictionary<Guid, bool> clientStatus = [];
    private readonly CaseAccessPolicy policy;
    private readonly CaseManager manager;
    private readonly CaseQueryService query;
    private readonly Guid service;
    private readonly Guid privateService;

    public CaseManagerTests()
    {
        CallAs(Admin, TenantRole.Administrator);
        data.Specializations[Private] = true;
        data.Specializations[Public] = false;
        data.Callers[Employee] = new CaseCaller(Guid.CreateVersion7(), new HashSet<Guid> { Public });
        data.Callers[ClientUser] = new CaseCaller(Client, new HashSet<Guid>());
        service = data.AddService(150m, null);
        privateService = data.AddService(80m, Private);

        clients.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Guid>() is var id && (id == Client || id == OtherClient) ? new ClientSummary(id, "Mario Rossi", null) : null);
        clients.EnsureEmployeeAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(Result.Success<Guid?>(Employee));
        clients.UpdateStatusAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            clientStatus[call.ArgAt<Guid>(0)] = call.ArgAt<bool>(1);
            return Result.Success();
        });
        customFields.ValidateAsync("case", Arg.Any<JsonElement?>(), Arg.Any<CancellationToken>()).Returns(Result.Success("{\"urgent\":true}"));
        permissions.HasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        policy = new CaseAccessPolicy(caller, data);
        guard.EnsureAsync(Arg.Any<string>(), Arg.Any<CaseResource>(), Arg.Any<CancellationToken>()).Returns(async call =>
            await policy.CanAccessAsync(call.ArgAt<CaseResource>(1), call.ArgAt<string>(0), call.ArgAt<CancellationToken>(2))
                ? Result.Success()
                : Result.Failure(Errors.Identity.PermissionDenied()));
        manager = new CaseManager(ManagerHarness.Runner(), data, clients, customFields, guard, policy, caller, accounts, messages, clock);
        query = new CaseQueryService(data, policy, permissions, clock);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => data.DisposeAsync();

    private void CallAs(Guid userId, params TenantRole[] roles)
    {
        caller.ActorType.Returns(ActorType.User);
        caller.UserId.Returns(userId);
        caller.Roles.Returns(roles);
    }

    private static OpenCaseRequest Request(Guid clientId, Guid serviceId, Guid? specialization = null, Guid? employee = null) =>
        new(clientId, serviceId, null, null, specialization, employee, null);

    private async Task<Guid> OpenAsync(Guid? serviceId = null, Guid? specialization = null) =>
        (await manager.OpenAsync(Request(Client, serviceId ?? service, specialization), Ct)).Value;

    private Case Stored(Guid id) => data.Cases.Single(@case => @case.Id == id);

    [Fact]
    public async Task Open_SnapshotsThePrice_NumbersPerYear_AndMakesTheClientActive()
    {
        var first = await OpenAsync();
        var second = await OpenAsync();

        var stored = Stored(first);
        (stored.Number, Stored(second).Number, stored.Price, stored.Currency, stored.StartedOn, stored.CustomFields)
            .ShouldBe(("2026-00001", "2026-00002", 150m, "EUR", new DateOnly(2026, 9, 30), "{\"urgent\":true}"));
        clientStatus[Client].ShouldBeTrue();
        await clients.Received().EnsureEmployeeAsync(Client, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Open_SpecializationDefaultsToTheService_AndOnlyAdministratorsChooseTheEmployee()
    {
        var id = await OpenAsync(privateService);
        Stored(id).SpecializationId.ShouldBe(Private);

        var chosen = Guid.CreateVersion7();
        await manager.OpenAsync(Request(Client, service, employee: chosen), Ct);
        await clients.Received(1).EnsureEmployeeAsync(Client, chosen, Arg.Any<CancellationToken>());

        CallAs(Employee, TenantRole.Employee);
        await manager.OpenAsync(Request(Client, service, employee: chosen), Ct);
        await clients.Received(2).EnsureEmployeeAsync(Client, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Open_InvalidReferences_AreFieldErrors()
    {
        data.Services[service] = data.Services[service] with { IsActive = false };

        var result = await manager.OpenAsync(Request(Guid.CreateVersion7(), service, Guid.CreateVersion7()), Ct);

        result.Error!.Code.ShouldBe(EventCodes.Cases.CaseInvalid);
        result.Error.ValidationErrors.Keys.ShouldBe(["clientId", "serviceId", "specializationId"], ignoreOrder: true);
        data.Cases.ShouldBeEmpty();
    }

    [Fact]
    public async Task Open_ByEmployee_OnlyForSpecializationsHeldOrNone()
    {
        CallAs(Employee, TenantRole.Employee);

        (await manager.OpenAsync(Request(Client, service), Ct)).IsSuccess.ShouldBeTrue();
        (await manager.OpenAsync(Request(Client, service, Public), Ct)).IsSuccess.ShouldBeTrue();
        (await manager.OpenAsync(Request(Client, privateService), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);
    }

    [Fact]
    public async Task Workflow_AdvanceBackCompleteAndPayments_RecomputeTheClientStatusAtTheEnd()
    {
        var id = await OpenAsync();

        (await manager.AddPaymentAsync(id, new AddCasePaymentRequest(50m, null, "acconto"), Ct)).IsSuccess.ShouldBeTrue();
        (await manager.AdvanceAsync(id, null, Ct)).IsSuccess.ShouldBeTrue();
        (await manager.GoBackAsync(id, null, Ct)).IsSuccess.ShouldBeTrue();
        (await manager.AdvanceAsync(id, null, Ct)).IsSuccess.ShouldBeTrue();
        (await manager.AdvanceAsync(id, null, Ct)).IsSuccess.ShouldBeTrue();
        (await manager.AdvanceAsync(id, null, Ct)).Error!.Code.ShouldBe(EventCodes.Cases.CaseCompletionRequired);
        clientStatus[Client].ShouldBeTrue();

        // Without an amount the rest of the price is received.
        (await manager.CompleteAsync(id, new CompleteCaseRequest(null, false, null), Ct)).IsSuccess.ShouldBeTrue();

        var detail = (await query.GetAsync(id, Ct)).Value;
        (detail.Status, detail.AmountPaid, detail.ExpiresOn, detail.CanManage, detail.CanDelete).ShouldBe(("Completed", 150m, (DateOnly?)new DateOnly(2026, 9, 30), true, true));
        detail.Payments.Select(payment => payment.Amount).ShouldBe([50m, 100m]);
        detail.History.Select(change => change.ToStatus).ShouldBe(["Inserted", "InProgress", "Inserted", "InProgress", "Sent", "Completed"]);
        detail.History[0].ChangedBy!.UserId.ShouldBe(Admin);
        clientStatus[Client].ShouldBeFalse();
        (await manager.UpdateAsync(id, new UpdateCaseRequest(null, null), Ct)).Error!.Code.ShouldBe(EventCodes.Cases.CaseIsCompleted);
    }

    [Fact]
    public async Task Delete_RecomputesTheClientStatus_EmployeesNotOnCompletedCases()
    {
        var kept = await OpenAsync();
        var deleted = await OpenAsync();
        (await manager.DeleteAsync(deleted, Ct)).IsSuccess.ShouldBeTrue();
        clientStatus[Client].ShouldBeTrue();
        (await query.GetAsync(deleted, Ct)).Error!.Code.ShouldBe(EventCodes.Cases.CaseNotFound);

        Stored(kept).Advance(Admin, null, clock.GetUtcNow());
        Stored(kept).Advance(Admin, null, clock.GetUtcNow());
        Stored(kept).Complete(0m, false, Admin, null, clock.GetUtcNow());
        CallAs(Employee, TenantRole.Employee);
        (await query.GetAsync(kept, Ct)).Value.CanDelete.ShouldBeFalse();
        (await manager.DeleteAsync(kept, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);
    }

    [Fact]
    public async Task Visibility_FollowsD04_AndClientsSeeTheirOwn()
    {
        var plain = await OpenAsync();
        var privateCase = await OpenAsync(privateService);
        var publicCase = await OpenAsync(specialization: Public);
        var otherClientCase = (await manager.OpenAsync(Request(OtherClient, service), Ct)).Value;

        CallAs(Employee, TenantRole.Employee);
        (await query.GetAsync(plain, Ct)).Value.CanManage.ShouldBeTrue();
        (await query.GetAsync(publicCase, Ct)).Value.CanManage.ShouldBeTrue();
        (await query.GetAsync(privateCase, Ct)).Error!.Code.ShouldBe(EventCodes.Cases.CaseNotFound);
        (await manager.AdvanceAsync(privateCase, null, Ct)).Error!.Code.ShouldBe(EventCodes.Cases.CaseNotFound);

        // A non-private specialization the employee lacks: visible, not manageable (403).
        data.Callers[Employee] = new CaseCaller(Guid.CreateVersion7(), new HashSet<Guid>());
        var employeePolicy = new CaseAccessPolicy(caller, data);
        (await employeePolicy.CanSeeAsync(new CaseResource(Client, Public, false, CaseStatus.Inserted), Ct)).ShouldBeTrue();
        (await employeePolicy.CanManageAsync(new CaseResource(Client, Public, false, CaseStatus.Inserted), Ct)).ShouldBeFalse();

        CallAs(ClientUser, TenantRole.Client);
        var clientPolicy = new CaseAccessPolicy(caller, data);
        (await clientPolicy.CanSeeAsync(new CaseResource(Client, Private, true, CaseStatus.Inserted), Ct)).ShouldBeTrue();
        (await clientPolicy.CanSeeAsync(new CaseResource(OtherClient, null, false, CaseStatus.Inserted), Ct)).ShouldBeFalse();
        (await clientPolicy.CanManageAsync(new CaseResource(Client, null, false, CaseStatus.Inserted), Ct)).ShouldBeFalse();
        (await clientPolicy.CanAccessAsync(new CaseResource(Client, null, false, CaseStatus.Inserted), "other.permission", Ct)).ShouldBeFalse();
        otherClientCase.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task UnknownCases_AreNotFound_AndUpdateKeepsTheCustomFields()
    {
        var id = await OpenAsync();

        (await manager.UpdateAsync(id, new UpdateCaseRequest(new DateOnly(2026, 12, 31), null), Ct)).IsSuccess.ShouldBeTrue();
        Stored(id).DueOn.ShouldBe(new DateOnly(2026, 12, 31));

        customFields.ValidateAsync("case", Arg.Any<JsonElement?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<string>(Errors.Host.ValidationFailed(new Dictionary<string, string[]> { ["customFields.x"] = ["x"] })));
        (await manager.UpdateAsync(id, new UpdateCaseRequest(null, null), Ct)).Error!.ValidationErrors.Keys.ShouldBe(["customFields.x"]);
        (await manager.OpenAsync(Request(Client, service), Ct)).IsFailure.ShouldBeTrue();

        var unknown = Guid.CreateVersion7();
        (await manager.AdvanceAsync(unknown, null, Ct)).Error!.Code.ShouldBe(EventCodes.Cases.CaseNotFound);
        (await manager.DeleteAsync(unknown, Ct)).Error!.Code.ShouldBe(EventCodes.Cases.CaseNotFound);
        (await query.GetAsync(unknown, Ct)).Error!.Code.ShouldBe(EventCodes.Cases.CaseNotFound);
    }

    [Fact]
    public async Task List_ScopeFollowsTheRole_AndTheTogglesStartOffOnlyForEmployees()
    {
        await OpenAsync();

        var page = (await query.ListAsync(new CaseListQuery(null, null, null, null, null, null, null, null, 1, 25), Ct)).Value;
        page.Items.ShouldHaveSingleItem().Validity.ShouldBe("Active");
        var admin = data.LastFilter!;
        (admin.Scope, admin.OnlyHeldOrUnspecialized, admin.IncludeCompleted, admin.Sort, admin.Descending)
            .ShouldBe((new CaseScope(true, null, null), false, true, CaseSort.StartedOn, true));

        CallAs(Employee, TenantRole.Employee);
        await query.ListAsync(new CaseListQuery(null, null, null, null, null, null, null, "client", 1, 25), Ct);
        var employee = data.LastFilter!;
        (employee.Scope, employee.OnlyHeldOrUnspecialized, employee.IncludeCompleted, employee.Sort, employee.Descending)
            .ShouldBe((new CaseScope(false, Employee, null), true, false, CaseSort.Client, false));
        await query.ListAsync(new CaseListQuery(" rossi ", "ISEE", Client, service, "Sent", true, true, "-amountPaid", 2, 10), Ct);
        var toggled = data.LastFilter!;
        (toggled.OnlyHeldOrUnspecialized, toggled.IncludeCompleted, toggled.ClientName, toggled.Status, toggled.Sort, toggled.Descending, toggled.Skip)
            .ShouldBe((false, true, "rossi", (CaseStatus?)CaseStatus.Sent, CaseSort.AmountPaid, true, 10));

        CallAs(ClientUser, TenantRole.Client);
        await new CaseQueryService(data, new CaseAccessPolicy(caller, data), permissions, clock)
            .ListAsync(new CaseListQuery(null, null, null, null, null, null, null, null, 1, 25), Ct);
        data.LastFilter!.Scope.ShouldBe(new CaseScope(false, null, Client));

        caller.Roles.Returns([]);
        await new CaseQueryService(data, new CaseAccessPolicy(caller, data), permissions, clock)
            .ListAsync(new CaseListQuery(null, null, null, null, null, null, null, null, 1, 25), Ct);
        data.LastFilter!.Scope.ShouldBe(CaseScope.None);
    }

    [Fact]
    public async Task List_InvalidQuery_IsRefused()
    {
        var invalid = await query.ListAsync(new CaseListQuery(new string('x', 201), null, null, null, "Gone", null, null, "age", 0, 500), Ct);

        invalid.Error!.ValidationErrors.Keys.ShouldBe(["page", "pageSize", "sort", "status", "search"], ignoreOrder: true);
    }

    [Fact]
    public async Task SendExpiryReminder_QueuesTheTemplateInTheClientLanguage()
    {
        var id = (await manager.OpenAsync(new OpenCaseRequest(Client, service, null, new DateOnly(2026, 10, 15), null, null, null), Ct)).Value;
        accounts.FindByPersonAsync(Client, Arg.Any<CancellationToken>())
            .Returns(new UserAccount(ClientUser, Client, "mario", "mario@example.test", true, true, [TenantRole.Client], "it"));
        messages.QueueAsync(Arg.Any<OutboundMessageRequest>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Guid.CreateVersion7()));

        (await manager.SendExpiryReminderAsync(id, Ct)).IsSuccess.ShouldBeTrue();

        await messages.Received(1).QueueAsync(
            Arg.Is<OutboundMessageRequest>(request => request.Channel == MessageChannel.Email && request.Recipient == "mario@example.test"
                && request.TemplateCode == MessageTemplates.CaseExpiryReminder && request.Language == "it"
                && (string?)request.Model["endDate"] == "15/10/2026" && (string?)request.Model["serviceName"] == "Service 0"
                && (string?)request.Model["name"] == "Mario Rossi" && request.RelatedEntityId == id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendExpiryReminder_NeedsAnEndDateAnEmailAndTheRightToManage()
    {
        var id = await OpenAsync();
        (await manager.SendExpiryReminderAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Cases.CaseHasNoEndDate);

        var dated = (await manager.OpenAsync(new OpenCaseRequest(Client, service, null, new DateOnly(2026, 10, 15), null, null, null), Ct)).Value;
        accounts.FindByPersonAsync(Client, Arg.Any<CancellationToken>())
            .Returns(new UserAccount(ClientUser, Client, "mario", null, true, true, [TenantRole.Client]));
        (await manager.SendExpiryReminderAsync(dated, Ct)).Error!.Code.ShouldBe(EventCodes.Cases.CaseClientHasNoEmail);

        CallAs(ClientUser, TenantRole.Client);
        (await manager.SendExpiryReminderAsync(dated, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);
        (await manager.SendExpiryReminderAsync(Guid.CreateVersion7(), Ct)).Error!.Code.ShouldBe(EventCodes.Cases.CaseNotFound);
        await messages.DidNotReceive().QueueAsync(Arg.Any<OutboundMessageRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, null, "Active")]
    [InlineData(true, "2026-09-30", "Active")]
    [InlineData(true, "2026-09-29", "Expired")]
    [InlineData(false, null, "Inactive")]
    public void Validity_IsTheLegacyClientBadge(bool isActive, string? expiresOn, string validity)
    {
        CaseQueryService.Validity(isActive, expiresOn is null ? null : DateOnly.Parse(expiresOn, System.Globalization.CultureInfo.InvariantCulture), new DateOnly(2026, 9, 30))
            .ShouldBe(validity);
    }
}

/// <summary>Cases in memory, with the services, specializations and callers the access rules need.</summary>
internal sealed class InMemoryCases : ICaseDataFactory, ICaseData
{
    private readonly Dictionary<int, int> numbers = [];

    public List<Case> Cases { get; } = [];

    public HashSet<Guid> Deleted { get; } = [];

    public Dictionary<Guid, CaseService> Services { get; } = [];

    /// <summary>Active Employee specializations → private.</summary>
    public Dictionary<Guid, bool> Specializations { get; } = [];

    public Dictionary<Guid, CaseCaller> Callers { get; } = [];

    public CaseFilter? LastFilter { get; private set; }

    public Guid AddService(decimal price, Guid? specializationId)
    {
        var id = Guid.CreateVersion7();
        Services[id] = new CaseService(id, "Service " + Services.Count, price, "EUR", specializationId, true);
        return id;
    }

    public Task<ICaseData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<ICaseData>(this);

    /// <summary>Applies no filter (the SQL is tested on PostgreSQL); records what was asked.</summary>
    public Task<(IReadOnlyList<CaseRow> Items, int Total)> PageAsync(CaseFilter filter, CancellationToken cancellationToken)
    {
        LastFilter = filter;
        var rows = Cases.Where(@case => !Deleted.Contains(@case.Id))
            .Select(@case => new CaseRow(
                @case.Id, @case.Number, @case.ClientId, "Mario Rossi", @case.ServiceId, Services[@case.ServiceId].Name, @case.SpecializationId,
                @case.SpecializationId is null ? null : "Spec", false, @case.Status, @case.IsRejected, @case.IsActive, @case.StartedOn, @case.DueOn,
                @case.ExpiresOn, @case.Price, @case.Currency, @case.AmountPaid, @case.CustomFields))
            .ToArray();
        return Task.FromResult<(IReadOnlyList<CaseRow>, int)>((rows, rows.Length));
    }

    public CaseDashboard Dashboard { get; set; } = new(0, [], [], [], null);

    public CaseScope? LastDashboardScope { get; private set; }

    public Task<CaseDashboard> DashboardAsync(CaseScope scope, DateOnly? from, DateOnly today, DateOnly horizon, int take, CancellationToken cancellationToken)
    {
        LastDashboardScope = scope;
        return Task.FromResult(Dashboard);
    }

    public Task<int> NextNumberAsync(int year, CancellationToken cancellationToken)
    {
        numbers[year] = numbers.GetValueOrDefault(year) + 1;
        return Task.FromResult(numbers[year]);
    }

    public Task<Case?> FindAsync(Guid id, bool readOnly, CancellationToken cancellationToken) =>
        Task.FromResult(Cases.SingleOrDefault(@case => @case.Id == id && !Deleted.Contains(@case.Id)));

    public Task<CaseNames> NamesAsync(Case @case, CancellationToken cancellationToken) =>
        Task.FromResult(new CaseNames("Mario Rossi", Services[@case.ServiceId].Name, null, 30, @case.SpecializationId is null ? null : "Spec", false));

    public Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(userIds.ToDictionary(id => id, id => "User " + id.ToString("N")[..4]));

    public Task<CaseService?> ServiceAsync(Guid serviceId, CancellationToken cancellationToken) => Task.FromResult(Services.GetValueOrDefault(serviceId));

    public Task<bool?> EmployeeSpecializationPrivacyAsync(Guid specializationId, CancellationToken cancellationToken) =>
        Task.FromResult(Specializations.TryGetValue(specializationId, out var isPrivate) ? (bool?)isPrivate : null);

    public Task<bool> IsPrivateSpecializationAsync(Guid specializationId, CancellationToken cancellationToken) =>
        Task.FromResult(Specializations.GetValueOrDefault(specializationId));

    public Task<CaseCaller> CallerAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Callers.GetValueOrDefault(userId) ?? new CaseCaller(null, new HashSet<Guid>()));

    public Task<bool> HasOpenCasesAsync(Guid clientId, DateOnly today, CancellationToken cancellationToken) =>
        Task.FromResult(Cases.Any(@case => !Deleted.Contains(@case.Id) && @case.ClientId == clientId
            && Case.CountsAsOpen(@case.IsActive, @case.Status, @case.ExpiresOn, today)));

    /// <summary>Client person → user.</summary>
    public Dictionary<Guid, Guid> ClientUsers { get; } = [];

    public Task<IReadOnlyList<Case>> ExpiredActiveAsync(DateOnly today, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Case>>(Cases.Where(@case => !Deleted.Contains(@case.Id) && @case.IsActive && @case.ExpiresOn <= today).ToArray());

    public Task<IReadOnlyList<Case>> ExpiringAsync(DateOnly today, DateOnly horizon, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Case>>(Cases
            .Where(@case => !Deleted.Contains(@case.Id) && @case.IsActive && @case.Status != CaseStatus.Completed && @case.EndsOn > today && @case.EndsOn <= horizon)
            .ToArray());

    public Task<IReadOnlyDictionary<Guid, Guid>> ClientUsersAsync(IReadOnlyCollection<Guid> clientIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(ClientUsers.Where(pair => clientIds.Contains(pair.Key)).ToDictionary());

    public void Add(Case @case) => Cases.Add(@case);

    public void Remove(Case @case) => Deleted.Add(@case.Id);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
