using System.Text.Json;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Directory;
using Auxilia.Application.Configuration.Public;
using Auxilia.Application.Directory;
using Auxilia.Application.Identity.Public;
using Auxilia.Application.Tests.Identity;
using Auxilia.Application.Tests.Platform;
using Auxilia.Contracts.Directory;
using Auxilia.Diagnostics;
using Auxilia.Domain.Directory;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace Auxilia.Application.Tests.Directory;

public sealed class ClientManagerTests : IAsyncDisposable
{
    private static readonly Guid Employee = Guid.CreateVersion7();
    private static readonly Guid OtherEmployee = Guid.CreateVersion7();
    private static readonly Guid Admin = Guid.CreateVersion7();

    private readonly InMemoryClientData data = new();
    private readonly IUserAccounts accounts = Substitute.For<IUserAccounts>();
    private readonly ICustomFieldValidator customFields = Substitute.For<ICustomFieldValidator>();
    private readonly ICurrentUser caller = Substitute.For<ICurrentUser>();
    private readonly ManualTimeProvider clock = new();
    private readonly Dictionary<Guid, UserAccount> users = [];
    private readonly ClientManager manager;
    private readonly ClientQueryService query;

    public ClientManagerTests()
    {
        AddUser(Employee, TenantRole.Employee);
        AddUser(OtherEmployee, TenantRole.Employee);
        AddUser(Admin, TenantRole.Administrator);
        CallAs(Admin, TenantRole.Administrator);

        customFields.ValidateAsync("client", Arg.Any<JsonElement?>(), Arg.Any<CancellationToken>()).Returns(Result.Success("{\"vip\":true}"));
        accounts.CreateAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), TenantRole.Client, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var id = Guid.CreateVersion7();
                users[id] = new UserAccount(id, call.ArgAt<Guid>(0), call.ArgAt<string>(1), call.ArgAt<string?>(2), call.ArgAt<bool>(4), false, [TenantRole.Client]);
                return Result.Success(id);
            });
        accounts.FindByPersonAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => users.Values.SingleOrDefault(user => user.PersonId == call.Arg<Guid>()));
        accounts.FindManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => users.Values.Where(user => call.Arg<IReadOnlyCollection<Guid>>().Contains(user.UserId)).ToArray());
        accounts.SetSignInAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            users[call.ArgAt<Guid>(0)] = users[call.ArgAt<Guid>(0)] with { CanSignIn = call.ArgAt<bool>(1) };
            return Result.Success();
        });
        accounts.UpdateAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        accounts.SendActivationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Success());

        manager = new ClientManager(ManagerHarness.Runner(), data, accounts, customFields, caller, clock, NullLogger<ClientManager>.Instance);
        query = new ClientQueryService(data, accounts, caller);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => data.DisposeAsync();

    private static CreateClientRequest Request(string email = "mario.rossi@example.test", string fiscalCode = "RSSMRA80A01H501U", Guid? employee = null) =>
        new("Mario", "Rossi", new DateOnly(1980, 1, 1), email, "3331234567", fiscalCode, null, employee);

    private static UpdateClientRequest Update(string fiscalCode = "RSSMRA80A01H501U", string userName = "mario.rossi") =>
        new("Mario", "Bianchi", new DateOnly(1980, 1, 1), "mario.bianchi@example.test", null, fiscalCode, userName, null);

    private void AddUser(Guid id, TenantRole role, bool canSignIn = true) =>
        users[id] = new UserAccount(id, Guid.CreateVersion7(), id.ToString("N")[..8], null, canSignIn, true, [role]);

    private void CallAs(Guid userId, params TenantRole[] roles)
    {
        caller.ActorType.Returns(ActorType.User);
        caller.UserId.Returns(userId);
        caller.Roles.Returns(roles);
    }

    private async Task<Guid> CreateAsync(CreateClientRequest? request = null) =>
        (await manager.CreateAsync(request ?? Request(), Ct)).Value.Id;

    [Fact]
    public async Task CreateAsync_ByAdministrator_CanSignIn_IsInvited_AndKeepsTheEmployeeGiven()
    {
        var created = (await manager.CreateAsync(Request(employee: Employee), Ct)).Value;

        created.InvitationSent.ShouldBeTrue();
        var person = data.People.Single();
        (person.Id, person.FiscalCode, person.CustomFields).ShouldBe((created.Id, "RSSMRA80A01H501U", "{\"vip\":true}"));
        data.Profiles.Single().EmployeeUserId.ShouldBe(Employee);
        await accounts.Received(1).CreateAsync(created.Id, "mario.rossi@example.test", "mario.rossi@example.test", TenantRole.Client, true, Arg.Any<CancellationToken>());
        await accounts.Received(1).SendActivationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_ByEmployee_CannotSignIn_IsNotInvited_AndIsAssignedToThatEmployee()
    {
        CallAs(Employee, TenantRole.Employee);

        var created = (await manager.CreateAsync(Request(employee: OtherEmployee), Ct)).Value;

        created.InvitationSent.ShouldBeFalse();
        data.Profiles.Single().EmployeeUserId.ShouldBe(Employee);
        await accounts.Received(1).CreateAsync(created.Id, Arg.Any<string>(), Arg.Any<string?>(), TenantRole.Client, false, Arg.Any<CancellationToken>());
        await accounts.DidNotReceiveWithAnyArgs().SendActivationAsync(default, Ct);
    }

    [Fact]
    public async Task CreateAsync_MissingFields_AreFieldErrorsTogether()
    {
        var result = await manager.CreateAsync(new CreateClientRequest("", "Rossi", null, "", null, "", null, null), Ct);

        result.Error!.Code.ShouldBe(EventCodes.Directory.PersonInvalid);
        result.Error.ValidationErrors.Keys.ShouldBe(["firstName", "email", "fiscalCode", "birthDate"], ignoreOrder: true);
        data.People.ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateAsync_DuplicateFiscalCode_IsAConflict()
    {
        await CreateAsync();

        var result = await manager.CreateAsync(Request(email: "other@example.test", fiscalCode: "rssmra80a01h501u"), Ct);

        result.Error!.Code.ShouldBe(EventCodes.Directory.FiscalCodeTaken);
    }

    [Fact]
    public async Task CreateAsync_EmployeeWhoIsNotAnActiveEmployee_IsRefused()
    {
        AddUser(OtherEmployee, TenantRole.Employee, canSignIn: false);

        (await manager.CreateAsync(Request(employee: OtherEmployee), Ct)).Error!.Code.ShouldBe(EventCodes.Directory.EmployeeInvalid);
        (await manager.CreateAsync(Request(employee: Admin), Ct)).Error!.Code.ShouldBe(EventCodes.Directory.EmployeeInvalid);
    }

    [Fact]
    public async Task CreateAsync_InvalidCustomFields_AreReturned()
    {
        customFields.ValidateAsync("client", Arg.Any<JsonElement?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<string>(Errors.Host.ValidationFailed(new Dictionary<string, string[]> { ["customFields.vip"] = ["x"] })));

        (await manager.CreateAsync(Request(), Ct)).Error!.ValidationErrors.Keys.ShouldBe(["customFields.vip"]);
    }

    [Fact]
    public async Task CreateAsync_InvitationFails_TheClientExistsAnyway()
    {
        accounts.SendActivationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Failure(Errors.Identity.UserNotFound()));

        var created = (await manager.CreateAsync(Request(), Ct)).Value;

        (created.InvitationSent, created.InvitationErrorCode).ShouldBe((false, $"AUX-{EventCodes.Identity.UserNotFound}"));
        data.People.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task UpdateAsync_ChangesPersonAndAccount_AndKeepsTheFiscalCodeUnique()
    {
        var id = await CreateAsync();
        var other = await CreateAsync(Request(email: "luigi@example.test", fiscalCode: "VRDLGU80A01H501X"));

        (await manager.UpdateAsync(id, Update(), Ct)).IsSuccess.ShouldBeTrue();
        (await manager.UpdateAsync(other, Update(), Ct)).Error!.Code.ShouldBe(EventCodes.Directory.FiscalCodeTaken);
        (await manager.UpdateAsync(Guid.CreateVersion7(), Update(), Ct)).Error!.Code.ShouldBe(EventCodes.Directory.ClientNotFound);

        data.People.Single(person => person.Id == id).LastName.ShouldBe("Bianchi");
        await accounts.Received(1).UpdateAsync(Arg.Any<Guid>(), "mario.rossi", "mario.bianchi@example.test", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetSignInAsync_EnablingNeedsAnEmployee_DisablingDoesNot()
    {
        CallAs(Employee, TenantRole.Employee);
        var withEmployee = await CreateAsync();
        CallAs(Admin, TenantRole.Administrator);
        var without = await CreateAsync(Request(email: "luigi@example.test", fiscalCode: "VRDLGU80A01H501X"));

        (await manager.SetSignInAsync(without, true, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.ClientEmployeeRequired);
        (await manager.SetSignInAsync(without, false, Ct)).IsSuccess.ShouldBeTrue();
        (await manager.SetSignInAsync(withEmployee, true, Ct)).IsSuccess.ShouldBeTrue();
        users.Values.Single(user => user.PersonId == withEmployee).CanSignIn.ShouldBeTrue();
    }

    [Fact]
    public async Task AssignAndUnassign_KeepTheHistory_AndCheckTheEmployee()
    {
        var id = await CreateAsync(Request(employee: Employee));
        clock.Advance(TimeSpan.FromHours(1));

        (await manager.AssignAsync(id, OtherEmployee, Ct)).IsSuccess.ShouldBeTrue();
        (await manager.AssignAsync(id, Admin, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.EmployeeInvalid);
        clock.Advance(TimeSpan.FromHours(1));
        (await manager.UnassignAsync(id, Ct)).IsSuccess.ShouldBeTrue();

        var detail = (await query.GetAsync(id, Ct)).Value;
        detail.Employee.ShouldBeNull();
        detail.Assignments.Select(item => (item.EmployeeUserId, item.EndedAt is null)).ShouldBe([(OtherEmployee, false), (Employee, false)]);
    }

    [Fact]
    public async Task DeleteAsync_HidesTheClient_DisablesSignIn_AndEndsTheAssignment()
    {
        var id = await CreateAsync(Request(employee: Employee));

        (await manager.DeleteAsync(id, Ct)).IsSuccess.ShouldBeTrue();

        data.Deleted.ShouldContain(id);
        (await query.GetAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.ClientNotFound);
        users.Values.Single(user => user.PersonId == id).CanSignIn.ShouldBeFalse();
        (await manager.DeleteAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.ClientNotFound);
    }

    [Fact]
    public async Task SetSpecializationsAsync_ReplacesTheSet_WithClientSpecializationsOnly()
    {
        var id = await CreateAsync();
        var userId = users.Values.Single(user => user.PersonId == id).UserId;
        var gold = Specialization.Create(Guid.CreateVersion7(), TenantRole.Client, new("Gold", null, null, null, false)).Value;
        var silver = Specialization.Create(Guid.CreateVersion7(), TenantRole.Client, new("Silver", null, null, null, false)).Value;
        data.Specializations.AddRange([gold, silver]);

        (await manager.SetSpecializationsAsync(id, [gold.Id, silver.Id], Ct)).IsSuccess.ShouldBeTrue();
        (await manager.SetSpecializationsAsync(id, [silver.Id], Ct)).IsSuccess.ShouldBeTrue();
        (await manager.SetSpecializationsAsync(id, [Guid.CreateVersion7()], Ct)).Error!.Code.ShouldBe(EventCodes.Directory.ClientSpecializationInvalid);

        gold.Members.ShouldBeEmpty();
        silver.Members.ShouldHaveSingleItem().UserId.ShouldBe(userId);
        (await query.GetAsync(id, Ct)).Value.Specializations.Select(item => item.Name).ShouldBe(["Silver"]);
    }

    [Fact]
    public async Task SendInvitationAsync_OnlyForAccountsNotActivated()
    {
        var id = await CreateAsync();

        (await manager.SendInvitationAsync(id, Ct)).Value.Sent.ShouldBeTrue();
        var userId = users.Values.Single(user => user.PersonId == id).UserId;
        users[userId] = users[userId] with { IsActivated = true };
        (await manager.SendInvitationAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.AccountAlreadyActivated);
        (await manager.SendInvitationAsync(Guid.CreateVersion7(), Ct)).Error!.Code.ShouldBe(EventCodes.Directory.ClientNotFound);
    }

    [Fact]
    public async Task ResetPasswordAsync_ReturnsTheTemporaryPassword()
    {
        var id = await CreateAsync();
        accounts.ResetPasswordAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(Result.Success(new AccountPasswordReset("Temp-1234")));

        (await manager.ResetPasswordAsync(id, false, Ct)).Value.TemporaryPassword.ShouldBe("Temp-1234");
        (await manager.ResetPasswordAsync(Guid.CreateVersion7(), false, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.ClientNotFound);
    }

    [Fact]
    public async Task ListAsync_MineIsTheCallersClients_AndInputIsValidated()
    {
        await CreateAsync(Request(employee: Employee));
        await CreateAsync(Request(email: "luigi@example.test", fiscalCode: "VRDLGU80A01H501X", employee: OtherEmployee));
        CallAs(Employee, TenantRole.Employee);

        (await query.ListAsync(new ClientListQuery("mine", null, null, null, null, null, null, null, 1, 25), Ct)).Value.TotalCount.ShouldBe(1);
        data.LastFilter!.EmployeeUserId.ShouldBe(Employee);
        (await query.ListAsync(new ClientListQuery(null, null, null, null, null, null, null, "-email", 1, 25), Ct)).Value.TotalCount.ShouldBe(2);
        (data.LastFilter.EmployeeUserId, data.LastFilter.Sort, data.LastFilter.Descending).ShouldBe((null, ClientSort.Email, true));

        var invalid = await query.ListAsync(new ClientListQuery("team", null, null, null, null, null, "Gone", "age", 0, 500), Ct);
        invalid.Error!.ValidationErrors.Keys.ShouldBe(["page", "pageSize", "view", "sort", "status"], ignoreOrder: true);
    }
}

/// <summary>Clients in memory: the page applies only the employee filter (the SQL filters are tested on PostgreSQL).</summary>
internal sealed class InMemoryClientData : IClientDataFactory, IClientData
{
    public List<Person> People { get; } = [];

    public List<ClientProfile> Profiles { get; } = [];

    public List<Specialization> Specializations { get; } = [];

    public HashSet<Guid> Deleted { get; } = [];

    public ClientFilter? LastFilter { get; private set; }

    public Task<IClientData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<IClientData>(this);

    public Task<(IReadOnlyList<ClientRow> Items, int Total)> PageAsync(ClientFilter filter, CancellationToken cancellationToken)
    {
        LastFilter = filter;
        var rows = Profiles
            .Where(profile => !Deleted.Contains(profile.Id) && (filter.EmployeeUserId is null || profile.EmployeeUserId == filter.EmployeeUserId))
            .Select(profile => People.Single(person => person.Id == profile.Id))
            .Select(person => new ClientRow(person.Id, person.FirstName, person.LastName, person.Email, person.Email!, person.Phone, person.FiscalCode,
                ClientStatus.Inactive, true, null, null, person.CustomFields))
            .ToArray();
        return Task.FromResult<(IReadOnlyList<ClientRow>, int)>((rows, rows.Length));
    }

    public Task<Person?> FindPersonAsync(Guid clientId, CancellationToken cancellationToken) =>
        Task.FromResult(Deleted.Contains(clientId) ? null : People.SingleOrDefault(person => person.Id == clientId && Profiles.Any(profile => profile.Id == clientId)));

    public Task<ClientProfile?> FindProfileAsync(Guid clientId, CancellationToken cancellationToken) =>
        Task.FromResult(Deleted.Contains(clientId) ? null : Profiles.SingleOrDefault(profile => profile.Id == clientId));

    public Task<bool> FiscalCodeTakenAsync(string fiscalCode, Guid? exceptPersonId, CancellationToken cancellationToken) =>
        Task.FromResult(People.Any(person => !Deleted.Contains(person.Id) && person.FiscalCode == fiscalCode && person.Id != exceptPersonId));

    public Task<IReadOnlyList<EmployeeName>> NamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EmployeeName>>(userIds.Select(id => new EmployeeName(id, "Employee " + id.ToString("N")[..4])).ToArray());

    public Task<IReadOnlyList<EmployeeName>> AssignableEmployeesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EmployeeName>>([]);

    public Task<IReadOnlyList<Specialization>> ClientSpecializationsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Specialization>>(Specializations.ToArray());

    public void Add(Person person) => People.Add(person);

    public void Add(ClientProfile profile) => Profiles.Add(profile);

    public void Remove(Person person) => Deleted.Add(person.Id);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
