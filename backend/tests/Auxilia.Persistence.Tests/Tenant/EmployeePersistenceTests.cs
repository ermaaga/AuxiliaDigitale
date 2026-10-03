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

/// <summary>F06 on PostgreSQL: employee list (filters, sorts, counts), one default employee, soft delete, administrators.</summary>
[Collection(TenantDatabaseGroup.Name)]
public sealed class EmployeePersistenceTests(TenantDatabaseFixture database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_FiltersSortsAndCounts_OnlyEmployeesNotDeleted()
    {
        var tag = Tag();
        var paola = await AddUserAsync("paola" + tag, "Paola", "Neri" + tag, "3331112222", [TenantRole.Employee]);
        var gino = await AddUserAsync("gino" + tag, "Gino", "Gialli" + tag, null, [TenantRole.Employee], isActive: false);
        var both = await AddUserAsync("both" + tag, "Anna", "Bianchi" + tag, null, [TenantRole.Employee, TenantRole.Administrator]);
        var admin = await AddUserAsync("admin" + tag, "Ada", "Capo" + tag, null, [TenantRole.Administrator]);
        await AddClientAsync(paola);
        await AddClientAsync(paola);
        var deletedClient = await AddClientAsync(paola);
        await SoftDeleteAsync(deletedClient);
        var tax = await AddSpecializationAsync("Tax " + tag, TenantRole.Employee, paola, both);
        await AddSpecializationAsync("Gold " + tag, TenantRole.Client, paola);

        await using (var data = new EmployeeData(AuditedContext()))
        {
            var page = await data.PageAsync(Filter(lastName: tag), Ct);
            page.Total.ShouldBe(3);
            page.Items.Select(row => row.UserId).ShouldBe([both, gino, paola]);
            var row = page.Items.Single(item => item.UserId == paola);
            (row.FirstName, row.UserName, row.Phone, row.CanSignIn, row.IsDefault, row.AssignedClients)
                .ShouldBe(("Paola", "paola" + tag, "3331112222", true, false, 2));
            row.ImageVersion.ShouldBeNull();
            var hash = await AddImageAsync(paola);
            (await data.PageAsync(Filter(lastName: tag), Ct)).Items.Single(item => item.UserId == paola).ImageVersion.ShouldBe(hash);
            (await data.ImageVersionAsync(paola, Ct)).ShouldBe(hash);
            (await data.ImageVersionAsync(gino, Ct)).ShouldBeNull();

            (await Ids(data, Filter(fullName: "paola neri" + tag))).ShouldBe([paola]);
            (await Ids(data, Filter(fullName: "Neri" + tag + " Paola"))).ShouldBe([paola]);
            (await Ids(data, Filter(email: "gino" + tag + "@"))).ShouldBe([gino]);
            (await Ids(data, Filter(userName: "BOTH" + tag))).ShouldBe([both]);
            (await Ids(data, Filter(phone: "1112", lastName: tag))).ShouldBe([paola]);
            (await Ids(data, Filter(lastName: tag, canSignIn: false))).ShouldBe([gino]);
            (await Ids(data, Filter(lastName: tag, canSignIn: true))).ShouldBe([both, paola]);
            (await Ids(data, Filter(lastName: tag + "%"))).ShouldBeEmpty();
            (await Ids(data, Filter(lastName: tag, sort: EmployeeSort.FullName))).ShouldBe([both, gino, paola]);
            (await Ids(data, Filter(lastName: tag, sort: EmployeeSort.UserName, descending: true))).ShouldBe([paola, gino, both]);
            (await data.PageAsync(Filter(lastName: tag) with { Skip = 1, Take = 1 }, Ct)).Items.Single().UserId.ShouldBe(gino);

            (await data.SpecializationsOfAsync([paola, both, gino], Ct)).Select(item => (item.UserId, item.SpecializationId))
                .ShouldBe([(paola, tax), (both, tax)], ignoreOrder: true);
            (await data.CountClientsInChargeAsync(paola, Ct)).ShouldBe(2);
            (await data.ClientsInChargeAsync(paola, Ct)).Count.ShouldBe(2);
            (await data.CreatedAtAsync(paola, Ct)).ShouldNotBeNull();
            (await data.FindPersonAsync(admin, Ct)).ShouldBeNull();
            (await data.AdministratorsAsync(Ct)).Select(name => name.UserId).ShouldContain(admin);
            (await data.AdministratorsAsync(Ct)).Select(name => name.UserId).ShouldNotContain(paola);

            data.Remove((await data.FindPersonAsync(gino, Ct))!);
            await data.SaveChangesAsync(Ct);
        }

        await using (var data = new EmployeeData(AuditedContext()))
        {
            (await Ids(data, Filter(lastName: tag))).ShouldBe([both, paola]);
            (await data.FindPersonAsync(gino, Ct)).ShouldBeNull();
            (await data.NamesAsync([gino], Ct)).Single().FullName.ShouldBe("Gino Gialli" + tag);
        }
    }

    [Fact]
    public async Task DefaultEmployee_IsUnique_AndServesNewClients()
    {
        var tag = Tag();
        var first = await AddUserAsync("first" + tag, "Prima", "Uno" + tag, null, [TenantRole.Employee]);
        var second = await AddUserAsync("second" + tag, "Seconda", "Due" + tag, null, [TenantRole.Employee]);
        var admin = await AddUserAsync("boss" + tag, "Ada", "Capo" + tag, null, [TenantRole.Administrator]);

        // The shared database may hold a default from another test: this test owns the flag while it runs.
        await using (var data = new EmployeeData(AuditedContext()))
        {
            if (await data.FindDefaultAsync(Ct) is { } previous)
            {
                previous.ClearDefault();
                await data.SaveChangesAsync(Ct);
            }

            var profile = EmployeeProfile.Create(first);
            profile.MakeDefault();
            profile.SetAdministrator(admin);
            data.Add(profile);
            await data.SaveChangesAsync(Ct);
        }

        await using (var db = AuditedContext())
        {
            var duplicate = EmployeeProfile.Create(second);
            duplicate.MakeDefault();
            db.Set<EmployeeProfile>().Add(duplicate);
            await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        }

        await using (var data = new EmployeeData(AuditedContext()))
        {
            var profile = (await data.FindDefaultAsync(Ct))!;
            (profile.Id, profile.AdministratorUserId).ShouldBe((first, admin));
            (await data.PageAsync(Filter(lastName: tag), Ct)).Items.Single(row => row.UserId == first).IsDefault.ShouldBeTrue();
        }

        await using (var clients = new ClientData(AuditedContext()))
        {
            (await clients.DefaultEmployeeAsync(Ct)).ShouldBe(first);
        }

        await using (var db = AuditedContext())
        {
            (await db.Set<User>().SingleAsync(user => user.Id == first, Ct)).SetActive(false);
            await db.SaveChangesAsync(Ct);
        }

        await using (var clients = new ClientData(AuditedContext()))
        {
            (await clients.DefaultEmployeeAsync(Ct)).ShouldBeNull();
        }

        await using (var data = new EmployeeData(AuditedContext()))
        {
            (await data.FindDefaultAsync(Ct))!.ClearDefault();
            await data.SaveChangesAsync(Ct);
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

    private static EmployeeFilter Filter(
        string? fullName = null, string? lastName = null, string? email = null, string? userName = null, string? phone = null,
        bool? canSignIn = null, EmployeeSort sort = EmployeeSort.LastName, bool descending = false) =>
        new(fullName, lastName, email, userName, phone, canSignIn, sort, descending, 0, 50);

    private static async Task<Guid[]> Ids(EmployeeData data, EmployeeFilter filter) =>
        (await data.PageAsync(filter, Ct)).Items.Select(row => row.UserId).ToArray();

    private async Task<Guid> AddUserAsync(string userName, string firstName, string lastName, string? phone, TenantRole[] roles, bool isActive = true)
    {
        await using var db = database.CreateContext();
        var person = Person.Create(
            Guid.CreateVersion7(), new PersonDetails(firstName, lastName, $"{userName}@example.test", new DateOnly(1980, 1, 1), phone, null), new DateOnly(2026, 10, 2)).Value;
        db.Set<Person>().Add(person);
        var user = User.Create(Guid.CreateVersion7(), person.Id, userName, person.Email, "it", roles, isActive).Value;
        db.Set<User>().Add(user);
        await db.SaveChangesAsync(Ct);
        return user.Id;
    }

    private async Task<Guid> AddClientAsync(Guid employee)
    {
        await using var db = database.CreateContext();
        var person = new Person(Guid.CreateVersion7(), "Cliente", "Prova", null);
        db.Set<Person>().Add(person);
        db.Set<ClientProfile>().Add(ClientProfile.Create(person.Id, employee, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(Ct);
        return person.Id;
    }

    private async Task SoftDeleteAsync(Guid personId)
    {
        await using var db = AuditedContext();
        db.Set<Person>().Remove(await db.Set<Person>().SingleAsync(person => person.Id == personId, Ct));
        await db.SaveChangesAsync(Ct);
    }

    private async Task<Guid> AddSpecializationAsync(string name, TenantRole role, params Guid[] members)
    {
        await using var db = database.CreateContext();
        var specialization = Specialization.Create(Guid.CreateVersion7(), role, new(name, null, null, null, false)).Value;
        specialization.AddMembers(members, DateTimeOffset.UtcNow);
        db.Set<Specialization>().Add(specialization);
        await db.SaveChangesAsync(Ct);
        return specialization.Id;
    }

    private async Task<string> AddImageAsync(Guid userId)
    {
        await using var db = database.CreateContext();
        var image = UserImage.Create(userId, [0xFF, 0xD8, 0x02], "image/jpeg");
        db.Set<UserImage>().Add(image);
        await db.SaveChangesAsync(Ct);
        return image.Hash;
    }
}
