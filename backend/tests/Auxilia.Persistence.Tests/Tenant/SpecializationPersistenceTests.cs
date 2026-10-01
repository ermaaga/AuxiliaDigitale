using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.Persistence.Tenant.Directory;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tests.Tenant;

/// <summary>F12 on PostgreSQL: specializations with their members (users of the role), names unique per role among active ones.</summary>
[Collection(TenantDatabaseGroup.Name)]
public sealed class SpecializationPersistenceTests(TenantDatabaseFixture database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Members_CandidatesAndActiveNames()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var anna = await AddUserAsync("anna" + tag, "Anna", "Rossi" + tag, TenantRole.Employee);
        var luca = await AddUserAsync("luca" + tag, "Luca", "Bianchi" + tag, TenantRole.Employee);
        var carla = await AddUserAsync("carla" + tag, "Carla", "Verdi" + tag, TenantRole.Client);
        var spec = new SpecializationSpec("Fisioterapia " + tag, "Riabilitazione", "fisio@example.test", "06 123456", true);
        var id = Guid.CreateVersion7();

        await using (var data = new SpecializationData(database.CreateContext()))
        {
            data.Add(Specialization.Create(id, TenantRole.Employee, spec).Value);
            await data.SaveChangesAsync(Ct);
            (await data.NameTakenAsync(TenantRole.Employee, spec.Name.ToUpperInvariant(), null, Ct)).ShouldBeTrue();
            (await data.NameTakenAsync(TenantRole.Employee, spec.Name, id, Ct)).ShouldBeFalse();
            (await data.NameTakenAsync(TenantRole.Client, spec.Name, null, Ct)).ShouldBeFalse();
            (await data.UsersWithRoleAsync([anna, luca, carla, Guid.CreateVersion7()], TenantRole.Employee, Ct)).ShouldBe([anna, luca], ignoreOrder: true);
        }

        await using (var data = new SpecializationData(database.CreateContext()))
        {
            (await data.CandidatesAsync(id, TenantRole.Employee, tag, 50, Ct)).Select(user => user.UserId).ShouldBe([anna, luca]);
            (await data.CandidatesAsync(id, TenantRole.Employee, "rossi" + tag + "_", 50, Ct)).ShouldBeEmpty();
            (await data.CandidatesAsync(id, TenantRole.Employee, "Anna Rossi" + tag, 50, Ct)).ShouldHaveSingleItem().UserId.ShouldBe(anna);
            (await data.CandidatesAsync(id, TenantRole.Employee, "Bianchi" + tag + " luca", 50, Ct)).ShouldHaveSingleItem().UserId.ShouldBe(luca);

            var specialization = (await data.FindAsync(id, Ct))!;
            specialization.AddMembers([anna, luca], DateTimeOffset.UtcNow).ShouldBe(2);
            await data.SaveChangesAsync(Ct);
        }

        await using (var data = new SpecializationData(database.CreateContext()))
        {
            var members = await data.MembersAsync(id, Ct);
            members.Select(user => (user.UserName, user.FullName)).ShouldBe([("anna" + tag, "Anna Rossi" + tag), ("luca" + tag, "Luca Bianchi" + tag)]);
            (await data.CandidatesAsync(id, TenantRole.Employee, tag, 50, Ct)).ShouldBeEmpty();
            (await data.ListAsync(TenantRole.Employee, Ct)).Single(item => item.Id == id).Members.Count.ShouldBe(2);

            var specialization = (await data.FindAsync(id, Ct))!;
            specialization.RemoveMember(anna).ShouldBeTrue();
            specialization.Deactivate();
            await data.SaveChangesAsync(Ct);
            (await data.FindAsync(id, Ct)).ShouldBeNull();
            (await data.ListAsync(null, Ct)).ShouldNotContain(item => item.Id == id);
        }

        // Deactivated: the name is free again, but two active ones with the same name are refused by the index.
        await using var db = database.CreateContext();
        db.Set<Specialization>().Add(Specialization.Create(Guid.CreateVersion7(), TenantRole.Employee, spec).Value);
        await db.SaveChangesAsync(Ct);
        db.Set<Specialization>().Add(Specialization.Create(Guid.CreateVersion7(), TenantRole.Employee, spec with { Name = spec.Name.ToLowerInvariant() }).Value);
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
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
