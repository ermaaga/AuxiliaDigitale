using Auxilia.Application.Abstractions.Directory;
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

public sealed class EmployeeManagerTests : IAsyncDisposable
{
    private static readonly Guid Admin = Guid.CreateVersion7();

    private readonly InMemoryEmployeeData data = new();
    private readonly IUserAccounts accounts = Substitute.For<IUserAccounts>();
    private readonly ManualTimeProvider clock = new();
    private readonly Dictionary<Guid, UserAccount> users = [];
    private readonly EmployeeManager manager;
    private readonly EmployeeQueryService query;

    public EmployeeManagerTests()
    {
        users[Admin] = new UserAccount(Admin, Guid.CreateVersion7(), "admin", null, true, true, [TenantRole.Administrator]);

        accounts.CreateAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), TenantRole.Employee, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var id = Guid.CreateVersion7();
                users[id] = new UserAccount(id, call.ArgAt<Guid>(0), call.ArgAt<string>(1), call.ArgAt<string?>(2), call.ArgAt<bool>(4), false, [TenantRole.Employee]);
                data.Employees[id] = call.ArgAt<Guid>(0);
                return Result.Success(id);
            });
        accounts.FindManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => users.Values.Where(user => call.Arg<IReadOnlyCollection<Guid>>().Contains(user.UserId)).ToArray());
        accounts.SetSignInAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            users[call.ArgAt<Guid>(0)] = users[call.ArgAt<Guid>(0)] with { CanSignIn = call.ArgAt<bool>(1) };
            return Result.Success();
        });
        accounts.UpdateAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        accounts.SendActivationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Success());

        manager = new EmployeeManager(ManagerHarness.Runner(), data, accounts, clock, NullLogger<EmployeeManager>.Instance);
        query = new EmployeeQueryService(data, accounts);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => data.DisposeAsync();

    private static CreateEmployeeRequest Request(string email = "paola.neri@example.test", string? fiscalCode = null, bool canSignIn = true) =>
        new("Paola", "Neri", new DateOnly(1985, 3, 4), email, "3331234567", fiscalCode, canSignIn);

    private static UpdateEmployeeRequest Update(string? fiscalCode = null, string userName = "paola") =>
        new("Paola", "Bianchi", new DateOnly(1985, 3, 4), "paola.bianchi@example.test", null, fiscalCode, userName);

    private async Task<Guid> CreateAsync(CreateEmployeeRequest? request = null) =>
        (await manager.CreateAsync(request ?? Request(), Ct)).Value.Id;

    private async Task<Guid> CreateAsync(string email) => await CreateAsync(Request(email: email));

    [Fact]
    public async Task CreateAsync_WithSignIn_CreatesPersonAccountAndProfile_AndInvites()
    {
        var created = (await manager.CreateAsync(Request(fiscalCode: "nrepla85c44h501x"), Ct)).Value;

        created.InvitationSent.ShouldBeTrue();
        var person = data.People.Single();
        (person.FirstName, person.FiscalCode).ShouldBe(("Paola", "NREPLA85C44H501X"));
        data.Profiles.Single().Id.ShouldBe(created.Id);
        await accounts.Received(1).CreateAsync(person.Id, "paola.neri@example.test", "paola.neri@example.test", TenantRole.Employee, true, Arg.Any<CancellationToken>());
        await accounts.Received(1).SendActivationAsync(created.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WithoutSignIn_IsNotInvited()
    {
        var created = (await manager.CreateAsync(Request(canSignIn: false), Ct)).Value;

        (created.InvitationSent, created.InvitationErrorCode).ShouldBe((false, null));
        await accounts.DidNotReceiveWithAnyArgs().SendActivationAsync(default, Ct);
    }

    [Fact]
    public async Task CreateAsync_InvitationFails_TheEmployeeExistsAnyway()
    {
        accounts.SendActivationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Failure(Errors.Identity.UserNotFound()));

        var created = (await manager.CreateAsync(Request(), Ct)).Value;

        (created.InvitationSent, created.InvitationErrorCode).ShouldBe((false, $"AUX-{EventCodes.Identity.UserNotFound}"));
        data.People.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task CreateAsync_MissingFields_AreFieldErrorsTogether_AndTheFiscalCodeIsOptionalButUnique()
    {
        var invalid = await manager.CreateAsync(new CreateEmployeeRequest("Paola", "", null, "", null, null, true), Ct);
        invalid.Error!.Code.ShouldBe(EventCodes.Directory.PersonInvalid);
        invalid.Error.ValidationErrors.Keys.ShouldBe(["lastName", "email", "birthDate"], ignoreOrder: true);

        await CreateAsync(Request(fiscalCode: "NREPLA85C44H501X"));
        (await manager.CreateAsync(Request(email: "other@example.test", fiscalCode: "NREPLA85C44H501X"), Ct)).Error!.Code
            .ShouldBe(EventCodes.Directory.FiscalCodeTaken);
        (await manager.CreateAsync(Request(email: "third@example.test"), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateAsync_AccountRefused_ReturnsTheIdentityError()
    {
        accounts.CreateAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), TenantRole.Employee, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Guid>(Errors.Identity.UserNameTaken()));

        (await manager.CreateAsync(Request(), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNameTaken);
        data.Profiles.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_ChangesPersonAndAccount_AndKeepsTheFiscalCodeUnique()
    {
        var id = await CreateAsync(Request(fiscalCode: "NREPLA85C44H501X"));
        var other = await CreateAsync(Request(email: "luigi@example.test", fiscalCode: "VRDLGU80A01H501X"));

        (await manager.UpdateAsync(id, Update("NREPLA85C44H501X"), Ct)).IsSuccess.ShouldBeTrue();
        (await manager.UpdateAsync(other, Update("NREPLA85C44H501X"), Ct)).Error!.Code.ShouldBe(EventCodes.Directory.FiscalCodeTaken);
        (await manager.UpdateAsync(id, Update() with { BirthDate = null }, Ct)).Error!.ValidationErrors.Keys.ShouldBe(["birthDate"]);
        (await manager.UpdateAsync(Admin, Update(), Ct)).Error!.Code.ShouldBe(EventCodes.Directory.EmployeeNotFound);

        data.People.Single(person => person.Id == data.Employees[id]).LastName.ShouldBe("Bianchi");
        await accounts.Received(1).UpdateAsync(id, "paola", "paola.bianchi@example.test", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MakeDefaultAsync_KeepsExactlyOneDefault_AmongEmployeesWhoCanSignIn()
    {
        var first = await CreateAsync("first@example.test");
        var second = await CreateAsync("second@example.test");
        var disabled = await CreateAsync(Request(email: "off@example.test", canSignIn: false));
        data.Profiles.RemoveAll(profile => profile.Id == second); // created before B-02: no profile yet

        (await manager.MakeDefaultAsync(first, Ct)).IsSuccess.ShouldBeTrue();
        (await manager.MakeDefaultAsync(first, Ct)).IsSuccess.ShouldBeTrue();
        (await manager.MakeDefaultAsync(second, Ct)).IsSuccess.ShouldBeTrue();
        (await manager.MakeDefaultAsync(disabled, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.DefaultEmployeeInactive);
        (await manager.MakeDefaultAsync(Admin, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.EmployeeNotFound);

        data.Profiles.Where(profile => profile.IsDefault).ShouldHaveSingleItem().Id.ShouldBe(second);
        (await query.GetAsync(second, Ct)).Value.IsDefault.ShouldBeTrue();
        (await query.GetAsync(first, Ct)).Value.IsDefault.ShouldBeFalse();
    }

    [Fact]
    public async Task SetSignInAsync_TheDefaultEmployeeCannotBeDisabled()
    {
        var id = await CreateAsync();
        var other = await CreateAsync("other@example.test");
        await manager.MakeDefaultAsync(id, Ct);

        (await manager.SetSignInAsync(id, false, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.EmployeeIsDefault);
        (await manager.SetSignInAsync(id, true, Ct)).IsSuccess.ShouldBeTrue();
        (await manager.SetSignInAsync(other, false, Ct)).IsSuccess.ShouldBeTrue();
        (await manager.SetSignInAsync(Guid.CreateVersion7(), true, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.EmployeeNotFound);

        users[other].CanSignIn.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteAsync_HandsTheClientsToTheDefaultEmployee_AndDisablesSignIn()
    {
        var fallback = await CreateAsync("default@example.test");
        var leaving = await CreateAsync("leaving@example.test");
        await manager.MakeDefaultAsync(fallback, Ct);
        var client = ClientProfile.Create(Guid.CreateVersion7(), leaving, clock.GetUtcNow());
        data.Clients.Add(client);
        clock.Advance(TimeSpan.FromHours(1));

        (await manager.DeleteAsync(fallback, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.EmployeeIsDefault);
        (await manager.DeleteAsync(leaving, Ct)).IsSuccess.ShouldBeTrue();

        client.EmployeeUserId.ShouldBe(fallback);
        client.Assignments.Select(item => (item.EmployeeUserId, item.EndedAt is null)).ShouldBe([(leaving, false), (fallback, true)]);
        data.Deleted.ShouldContain(data.Employees[leaving]);
        users[leaving].CanSignIn.ShouldBeFalse();
        (await query.GetAsync(leaving, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.EmployeeNotFound);
        (await manager.DeleteAsync(leaving, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.EmployeeNotFound);
    }

    [Fact]
    public async Task DeleteAsync_WithoutAnActiveDefault_LeavesTheClientsWithNobody()
    {
        var leaving = await CreateAsync();
        var client = ClientProfile.Create(Guid.CreateVersion7(), leaving, clock.GetUtcNow());
        data.Clients.Add(client);

        (await manager.DeleteAsync(leaving, Ct)).IsSuccess.ShouldBeTrue();

        client.EmployeeUserId.ShouldBeNull();
        client.Assignments.ShouldHaveSingleItem().EndedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task SetSpecializationsAsync_ReplacesTheSet_WithEmployeeSpecializationsOnly()
    {
        var id = await CreateAsync();
        var tax = Specialization.Create(Guid.CreateVersion7(), TenantRole.Employee, new("Tax", null, null, null, false)).Value;
        var legal = Specialization.Create(Guid.CreateVersion7(), TenantRole.Employee, new("Legal", null, null, null, false)).Value;
        data.Specializations.AddRange([tax, legal]);

        (await manager.SetSpecializationsAsync(id, [tax.Id, legal.Id], Ct)).IsSuccess.ShouldBeTrue();
        (await manager.SetSpecializationsAsync(id, [legal.Id, legal.Id], Ct)).IsSuccess.ShouldBeTrue();
        (await manager.SetSpecializationsAsync(id, [Guid.CreateVersion7()], Ct)).Error!.Code.ShouldBe(EventCodes.Directory.EmployeeSpecializationInvalid);
        (await manager.SetSpecializationsAsync(id, Enumerable.Range(0, 51).Select(_ => Guid.CreateVersion7()).ToArray(), Ct)).Error!.Code
            .ShouldBe(EventCodes.Directory.EmployeeSpecializationInvalid);
        (await manager.SetSpecializationsAsync(Admin, [], Ct)).Error!.Code.ShouldBe(EventCodes.Directory.EmployeeNotFound);

        tax.Members.ShouldBeEmpty();
        legal.Members.ShouldHaveSingleItem().UserId.ShouldBe(id);
        (await query.GetAsync(id, Ct)).Value.Specializations.Select(item => item.Name).ShouldBe(["Legal"]);
    }

    [Fact]
    public async Task SetAdministratorAsync_OnlyActiveAdministrators_AndCanBeRemoved()
    {
        var id = await CreateAsync();
        var other = await CreateAsync("other@example.test");
        data.Profiles.RemoveAll(profile => profile.Id == id);

        (await manager.SetAdministratorAsync(id, null, Ct)).IsSuccess.ShouldBeTrue();
        data.Profiles.ShouldNotContain(profile => profile.Id == id);
        (await manager.SetAdministratorAsync(id, other, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.AdministratorInvalid);
        (await manager.SetAdministratorAsync(id, Admin, Ct)).IsSuccess.ShouldBeTrue();
        (await query.GetAsync(id, Ct)).Value.Administrator!.UserId.ShouldBe(Admin);

        users[Admin] = users[Admin] with { CanSignIn = false };
        (await manager.SetAdministratorAsync(other, Admin, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.AdministratorInvalid);
        (await manager.SetAdministratorAsync(id, null, Ct)).IsSuccess.ShouldBeTrue();
        (await query.GetAsync(id, Ct)).Value.Administrator.ShouldBeNull();
        (await manager.SetAdministratorAsync(Admin, null, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.EmployeeNotFound);
    }

    [Fact]
    public async Task SendInvitationAsync_OnlyForAccountsNotActivated()
    {
        var id = await CreateAsync(Request(canSignIn: false));

        (await manager.SendInvitationAsync(id, Ct)).Value.Sent.ShouldBeTrue();
        users[id] = users[id] with { IsActivated = true };
        (await manager.SendInvitationAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.AccountAlreadyActivated);
        (await manager.SendInvitationAsync(Guid.CreateVersion7(), Ct)).Error!.Code.ShouldBe(EventCodes.Directory.EmployeeNotFound);

        accounts.SendActivationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Failure(Errors.Identity.UserNotFound()));
        users[id] = users[id] with { IsActivated = false };
        (await manager.SendInvitationAsync(id, Ct)).Value.ShouldBe(new EmployeeInvitationResponse(false, $"AUX-{EventCodes.Identity.UserNotFound}"));
    }

    [Fact]
    public async Task ResetPasswordAsync_ReturnsTheTemporaryPassword()
    {
        var id = await CreateAsync();
        accounts.ResetPasswordAsync(id, false, Arg.Any<CancellationToken>()).Returns(Result.Success(new AccountPasswordReset("Temp-1234")));
        accounts.ResetPasswordAsync(id, true, Arg.Any<CancellationToken>()).Returns(Result.Failure<AccountPasswordReset>(Errors.Identity.UserNotFound()));

        (await manager.ResetPasswordAsync(id, false, Ct)).Value.TemporaryPassword.ShouldBe("Temp-1234");
        (await manager.ResetPasswordAsync(id, true, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNotFound);
        (await manager.ResetPasswordAsync(Admin, false, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.EmployeeNotFound);
    }

    [Fact]
    public async Task GetAsync_ShowsAccountProfileAndWorkload()
    {
        var id = await CreateAsync(Request(fiscalCode: "NREPLA85C44H501X"));
        data.Clients.Add(ClientProfile.Create(Guid.CreateVersion7(), id, clock.GetUtcNow()));
        data.Clients.Add(ClientProfile.Create(Guid.CreateVersion7(), id, clock.GetUtcNow()));

        var detail = (await query.GetAsync(id, Ct)).Value;

        (detail.Id, detail.PersonId, detail.UserName, detail.CanSignIn, detail.IsActivated).ShouldBe((id, data.Employees[id], "paola.neri@example.test", true, false));
        (detail.FiscalCode, detail.BirthDate, detail.CreatedAt).ShouldBe(("NREPLA85C44H501X", new DateOnly(1985, 3, 4), InMemoryEmployeeData.CreatedAt));
        detail.Workload.AssignedClients.ShouldBe(2);
        detail.ImageVersion.ShouldBeNull();
        data.Images[id] = "abc123";
        (await query.GetAsync(id, Ct)).Value.ImageVersion.ShouldBe("abc123");
        (await query.GetAsync(Guid.CreateVersion7(), Ct)).Error!.Code.ShouldBe(EventCodes.Directory.EmployeeNotFound);
    }

    [Fact]
    public async Task ListAsync_MapsRows_WithTheirSpecializations_AndValidatesTheInput()
    {
        var id = await CreateAsync();
        var tax = Specialization.Create(Guid.CreateVersion7(), TenantRole.Employee, new("Tax", null, null, null, false)).Value;
        data.Specializations.Add(tax);
        await manager.SetSpecializationsAsync(id, [tax.Id], Ct);

        var page = (await query.ListAsync(new EmployeeListQuery(" Paola ", null, null, null, null, "inactive", "-userName", 2, 10), Ct)).Value;
        (page.Page, page.PageSize, page.TotalCount).ShouldBe((2, 10, 1));
        page.Items.Single().Specializations.Select(item => item.Name).ShouldBe(["Tax"]);
        page.Items.Single().ImageVersion.ShouldBeNull();
        (data.LastFilter!.FullName, data.LastFilter.CanSignIn, data.LastFilter.Sort, data.LastFilter.Descending, data.LastFilter.Skip)
            .ShouldBe(("Paola", false, EmployeeSort.UserName, true, 10));
        (await query.ListAsync(new EmployeeListQuery(null, null, null, null, null, "active", null, 1, 25), Ct)).IsSuccess.ShouldBeTrue();
        (data.LastFilter.CanSignIn, data.LastFilter.Sort).ShouldBe((true, EmployeeSort.LastName));

        var invalid = await query.ListAsync(new EmployeeListQuery(null, new string('x', 201), null, null, null, "Gone", "age", 0, 500), Ct);
        invalid.Error!.ValidationErrors.Keys.ShouldBe(["page", "pageSize", "sort", "status", "search"], ignoreOrder: true);
    }

    [Fact]
    public async Task AdministratorsAsync_MapsTheNames()
    {
        data.Administrators.Add(new EmployeeName(Admin, "Anna Admin"));

        (await query.AdministratorsAsync(Ct)).ShouldBe([new EmployeeAdministratorResponse(Admin, "Anna Admin")]);
    }

    [Fact]
    public async Task SpecializationsAsync_MapsTheEmployeeSpecializations()
    {
        var tax = Specialization.Create(Guid.CreateVersion7(), TenantRole.Employee, new("Tax", null, null, null, false)).Value;
        data.Specializations.Add(tax);

        (await query.SpecializationsAsync(Ct)).ShouldBe([new EmployeeSpecializationResponse(tax.Id, "Tax")]);
    }
}

/// <summary>Employees in memory: <see cref="Employees"/> maps the user id of every employee to its person.</summary>
internal sealed class InMemoryEmployeeData : IEmployeeDataFactory, IEmployeeData
{
    public static readonly DateTimeOffset CreatedAt = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    public Dictionary<Guid, Guid> Employees { get; } = [];

    public List<Person> People { get; } = [];

    public List<EmployeeProfile> Profiles { get; } = [];

    public List<ClientProfile> Clients { get; } = [];

    public List<Specialization> Specializations { get; } = [];

    public List<EmployeeName> Administrators { get; } = [];

    public HashSet<Guid> Deleted { get; } = [];

    /// <summary>Profile picture hashes by user id.</summary>
    public Dictionary<Guid, string> Images { get; } = [];

    public EmployeeFilter? LastFilter { get; private set; }

    public Task<IEmployeeData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<IEmployeeData>(this);

    public Task<(IReadOnlyList<EmployeeRow> Items, int Total)> PageAsync(EmployeeFilter filter, CancellationToken cancellationToken)
    {
        LastFilter = filter;
        var rows = Employees
            .Where(employee => !Deleted.Contains(employee.Value))
            .Select(employee => (UserId: employee.Key, Person: People.Single(person => person.Id == employee.Value)))
            .Select(employee => new EmployeeRow(employee.UserId, employee.Person.FirstName, employee.Person.LastName, employee.Person.Email,
                employee.Person.Email!, employee.Person.Phone, true, Profiles.Any(profile => profile.Id == employee.UserId && profile.IsDefault),
                Clients.Count(client => client.EmployeeUserId == employee.UserId), Images.GetValueOrDefault(employee.UserId)))
            .ToArray();
        return Task.FromResult<(IReadOnlyList<EmployeeRow>, int)>((rows, rows.Length));
    }

    public Task<IReadOnlyList<EmployeeSpecializationRow>> SpecializationsOfAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EmployeeSpecializationRow>>(Specializations
            .SelectMany(specialization => specialization.Members
                .Where(member => userIds.Contains(member.UserId))
                .Select(member => new EmployeeSpecializationRow(member.UserId, specialization.Id, specialization.Name)))
            .ToArray());

    public Task<string?> ImageVersionAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Images.GetValueOrDefault(userId));

    public Task<Person?> FindPersonAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Employees.TryGetValue(userId, out var personId) && !Deleted.Contains(personId)
            ? People.SingleOrDefault(person => person.Id == personId)
            : null);

    public Task<EmployeeProfile?> FindProfileAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Profiles.SingleOrDefault(profile => profile.Id == userId));

    public Task<EmployeeProfile?> FindDefaultAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Profiles.SingleOrDefault(profile => profile.IsDefault));

    public Task<DateTimeOffset?> CreatedAtAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<DateTimeOffset?>(CreatedAt);

    public Task<IReadOnlyList<ClientProfile>> ClientsInChargeAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ClientProfile>>(Clients.Where(client => client.EmployeeUserId == userId).ToArray());

    public Task<int> CountClientsInChargeAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Clients.Count(client => client.EmployeeUserId == userId));

    public Task<bool> FiscalCodeTakenAsync(string fiscalCode, Guid? exceptPersonId, CancellationToken cancellationToken) =>
        Task.FromResult(People.Any(person => !Deleted.Contains(person.Id) && person.FiscalCode == fiscalCode && person.Id != exceptPersonId));

    public Task<IReadOnlyList<EmployeeName>> AdministratorsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EmployeeName>>(Administrators.ToArray());

    public Task<IReadOnlyList<EmployeeName>> NamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EmployeeName>>(userIds.Select(id => new EmployeeName(id, "User " + id.ToString("N")[..4])).ToArray());

    public Task<IReadOnlyList<Specialization>> EmployeeSpecializationsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Specialization>>(Specializations.ToArray());

    public void Add(Person person) => People.Add(person);

    public void Add(EmployeeProfile profile) => Profiles.Add(profile);

    public void Remove(Person person) => Deleted.Add(person.Id);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
