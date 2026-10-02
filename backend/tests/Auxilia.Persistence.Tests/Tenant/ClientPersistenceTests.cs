using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Directory;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.Persistence.Tenant;
using Auxilia.Persistence.Tenant.Directory;
using Auxilia.Persistence.Tenant.Interceptors;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

using NSubstitute;

namespace Auxilia.Persistence.Tests.Tenant;

/// <summary>F05 on PostgreSQL: client lists (filters, sorts, mine), soft delete, unique fiscal code, assignment history.</summary>
[Collection(TenantDatabaseGroup.Name)]
public sealed class ClientPersistenceTests(TenantDatabaseFixture database)
{
    private static readonly DateOnly Today = new(2026, 10, 2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lists_FilterSortAndPage_OnlyTheClientsNotDeleted()
    {
        var tag = Tag();
        var employee = await AddUserAsync("emp" + tag, "Paola", "Neri" + tag, TenantRole.Employee);
        var other = await AddUserAsync("oth" + tag, "Gino", "Gialli" + tag, TenantRole.Employee);
        var mario = await AddClientAsync(tag, "Mario", "Rossi", "3331112222", employee);
        var anna = await AddClientAsync(tag, "Anna", "Bianchi", null, employee);
        var luca = await AddClientAsync(tag, "Luca", "Verdi", "06 555 1234", other);

        await using (var data = new ClientData(AuditedContext()))
        {
            var mine = await data.PageAsync(Filter(employee), Ct);
            mine.Total.ShouldBe(2);
            mine.Items.Select(row => row.Id).ShouldBe([anna, mario]);
            mine.Items[0].EmployeeName.ShouldBe("Paola Neri" + tag);
            (mine.Items[1].UserName, mine.Items[1].CanSignIn, mine.Items[1].Status, mine.Items[1].CustomFields).ShouldBe(
                ($"rossi{tag}@example.test", true, ClientStatus.Inactive, "{}"));

            (await Ids(data, Filter(fullName: "mario rossi" + tag))).ShouldBe([mario]);
            (await Ids(data, Filter(fullName: "Rossi" + tag + " Mario"))).ShouldBe([mario]);
            (await Ids(data, Filter(lastName: "VERDI" + tag))).ShouldBe([luca]);
            (await Ids(data, Filter(email: "bianchi" + tag))).ShouldBe([anna]);
            (await Ids(data, Filter(userName: "rossi" + tag))).ShouldBe([mario]);
            (await Ids(data, Filter(phone: "555", lastName: tag))).ShouldBe([luca]);
            (await Ids(data, Filter(lastName: tag + "%"))).ShouldBeEmpty();
            (await Ids(data, Filter(lastName: tag, sort: ClientSort.FullName))).ShouldBe([anna, luca, mario]);
            (await Ids(data, Filter(lastName: tag, sort: ClientSort.LastName, descending: true))).ShouldBe([luca, mario, anna]);
            (await data.PageAsync(Filter(lastName: tag) with { Skip = 1, Take = 1 }, Ct)).Items.Single().Id.ShouldBe(mario);
            (await data.PageAsync(Filter(lastName: tag, status: ClientStatus.Active), Ct)).Total.ShouldBe(0);

            data.Remove((await data.FindPersonAsync(luca, Ct))!);
            await data.SaveChangesAsync(Ct);
        }

        await using (var data = new ClientData(AuditedContext()))
        {
            (await Ids(data, Filter(lastName: tag))).ShouldBe([anna, mario]);
            (await data.FindPersonAsync(luca, Ct)).ShouldBeNull();
            (await data.FindProfileAsync(luca, Ct)).ShouldBeNull();
            (await data.FindPersonAsync(employee, Ct)).ShouldBeNull();
            (await data.NamesAsync([employee, other], Ct)).Select(name => name.FullName).ShouldBe(["Paola Neri" + tag, "Gino Gialli" + tag], ignoreOrder: true);
            (await data.AssignableEmployeesAsync(Ct)).Select(name => name.UserId).ShouldContain(employee);
            (await data.AssignableEmployeesAsync(Ct)).Select(name => name.UserId).ShouldNotContain(mario);
        }
    }

    [Fact]
    public async Task FiscalCode_IsUniqueAmongThePeopleNotDeleted()
    {
        var tag = Tag();
        var first = await AddClientAsync(tag, "Mario", "Rossi", null, null);
        string fiscalCode;

        await using (var data = new ClientData(AuditedContext()))
        {
            fiscalCode = (await data.FindPersonAsync(first, Ct))!.FiscalCode!;
            (await data.FiscalCodeTakenAsync(fiscalCode, null, Ct)).ShouldBeTrue();
            (await data.FiscalCodeTakenAsync(fiscalCode, first, Ct)).ShouldBeFalse();
        }

        await using (var db = database.CreateContext())
        {
            db.Set<Person>().Add(Person.Create(Guid.CreateVersion7(), Details(tag, "Luigi", "Rossi", null, fiscalCode), Today).Value);
            await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        }

        await using (var data = new ClientData(AuditedContext()))
        {
            data.Remove((await data.FindPersonAsync(first, Ct))!);
            await data.SaveChangesAsync(Ct);
            (await data.FiscalCodeTakenAsync(fiscalCode, null, Ct)).ShouldBeFalse();
        }

        // A deleted client frees the fiscal code.
        await using var again = database.CreateContext();
        again.Set<Person>().Add(Person.Create(Guid.CreateVersion7(), Details(tag, "Luigi", "Rossi", null, fiscalCode), Today).Value);
        await again.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Assignments_KeepTheHistory_WithOneOpenAssignment()
    {
        var tag = Tag();
        var first = await AddUserAsync("a" + tag, "Anna", "Uno" + tag, TenantRole.Employee);
        var second = await AddUserAsync("b" + tag, "Bruno", "Due" + tag, TenantRole.Employee);
        var client = await AddClientAsync(tag, "Mario", "Rossi", null, first);

        await using (var data = new ClientData(AuditedContext()))
        {
            var profile = (await data.FindProfileAsync(client, Ct))!;
            profile.Assign(second, DateTimeOffset.UtcNow.AddMinutes(1)).ShouldBeTrue();
            profile.UpdateStatus(true, DateTimeOffset.UtcNow);
            await data.SaveChangesAsync(Ct);
        }

        await using (var data = new ClientData(AuditedContext()))
        {
            var profile = (await data.FindProfileAsync(client, Ct))!;
            (profile.EmployeeUserId, profile.Status).ShouldBe((second, ClientStatus.Active));
            profile.Assignments.Select(item => (item.EmployeeUserId, item.EndedAt is null)).ShouldBe([(first, false), (second, true)]);
            (await Ids(data, Filter(second))).ShouldBe([client]);
        }
    }

    /// <summary>A context as the factory builds it: deletes of people become soft deletes.</summary>
    private TenantDbContext AuditedContext()
    {
        var user = Substitute.For<ICurrentUser>();
        user.ActorType.Returns(ActorType.User);
        return new TenantDbContext(TenantDbContextOptions.Create(database.DataSource, new TenantAuditInterceptor(user, TimeProvider.System)));
    }

    private static string Tag() => new(Guid.NewGuid().ToString("N")[..8].Select(c => char.IsDigit(c) ? (char)('a' + c - '0') : c).ToArray());

    private static ClientFilter Filter(
        Guid? employee = null, string? fullName = null, string? lastName = null, string? email = null, string? userName = null,
        string? phone = null, ClientStatus? status = null, ClientSort sort = ClientSort.LastName, bool descending = false) =>
        new(employee, fullName, lastName, email, userName, phone, status, sort, descending, 0, 50);

    private static async Task<Guid[]> Ids(ClientData data, ClientFilter filter) =>
        (await data.PageAsync(filter, Ct)).Items.Select(row => row.Id).ToArray();

    private static PersonDetails Details(string tag, string firstName, string lastName, string? phone, string fiscalCode) =>
        new(firstName, lastName + tag, $"{lastName.ToLowerInvariant()}{tag}@example.test", new DateOnly(1980, 1, 1), phone, fiscalCode);

    /// <summary>A valid fiscal code nobody else has (random letters and digits in the right places).</summary>
    private static string NewFiscalCode()
    {
        static char Letter() => (char)('A' + Random.Shared.Next(26));
        static char Digit() => (char)('0' + Random.Shared.Next(10));
        return string.Concat(
            new string([Letter(), Letter(), Letter(), Letter(), Letter(), Letter()]),
            new string([Digit(), Digit()]), "A", new string([Digit(), Digit()]), new string([Letter()]),
            new string([Digit(), Digit(), Digit()]), new string([Letter()]));
    }

    private async Task<Guid> AddClientAsync(string tag, string firstName, string lastName, string? phone, Guid? employee)
    {
        await using var db = database.CreateContext();
        var person = Person.Create(Guid.CreateVersion7(), Details(tag, firstName, lastName, phone, NewFiscalCode()), Today).Value;
        db.Set<Person>().Add(person);
        db.Set<ClientProfile>().Add(ClientProfile.Create(person.Id, employee, DateTimeOffset.UtcNow));
        db.Set<User>().Add(User.Create(Guid.CreateVersion7(), person.Id, person.Email!, person.Email, "it", [TenantRole.Client], isActive: true).Value);
        await db.SaveChangesAsync(Ct);
        return person.Id;
    }

    private async Task<Guid> AddUserAsync(string userName, string firstName, string lastName, TenantRole role)
    {
        await using var db = database.CreateContext();
        var person = new Person(Guid.CreateVersion7(), firstName, lastName, null);
        db.Set<Person>().Add(person);
        var user = User.Create(Guid.CreateVersion7(), person.Id, userName, null, "it", [role], isActive: true).Value;
        db.Set<User>().Add(user);
        await db.SaveChangesAsync(Ct);
        return user.Id;
    }
}
