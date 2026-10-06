using Auxilia.Application.Abstractions.Documents;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Documents;
using Auxilia.Domain.Identity;
using Auxilia.Persistence.Tenant.Documents;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Persistence.Tests.Tenant;

/// <summary>F10 on PostgreSQL for the documents without a case an employee sees (the list SQL, D-04).</summary>
[Collection(TenantDatabaseGroup.Name)]
public sealed class DocumentPersistenceTests(TenantDatabaseFixture database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EmployeeScope_HidesClientsOfPrivateSpecializationsOfOthers_UnlessAssigned()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var employee = await AddUserAsync("emp" + tag, "Paola", "Neri", TenantRole.Employee);
        var hidden = await AddClientAsync(tag, "Hidden", null);
        var shared = await AddClientAsync(tag, "Shared", null);
        var free = await AddClientAsync(tag, "Free", null);
        var assigned = await AddClientAsync(tag, "Assigned", employee);

        // Private specialization without the employee: its clients are hidden unless assigned to the employee.
        await AddSpecializationAsync(tag + "a", isPrivate: true, [hidden.UserId, assigned.UserId]);

        // Private specialization the employee holds, and a public one: their clients stay visible.
        await AddSpecializationAsync(tag + "b", isPrivate: true, [shared.UserId, employee]);
        await AddSpecializationAsync(tag + "c", isPrivate: false, [free.UserId]);

        foreach (var client in new[] { hidden, shared, free, assigned })
        {
            await AddDocumentAsync(client.PersonId);
        }

        await using var data = new DocumentData(database.CreateContext());
        var filter = new DocumentFilter(new DocumentScope(false, employee), null, null, null, tag, null, null, null, null, null,
            DocumentSort.Client, false, 0, 25);
        var page = await data.PageAsync(filter, Ct);
        var everything = await data.PageAsync(filter with { Scope = new DocumentScope(true, null) }, Ct);

        page.Items.Select(row => row.Document.ClientId).ShouldBe([shared.PersonId, free.PersonId, assigned.PersonId], ignoreOrder: true);
        page.Total.ShouldBe(3);
        everything.Total.ShouldBe(4);
    }

    private async Task<(Guid PersonId, Guid UserId)> AddClientAsync(string tag, string firstName, Guid? employee)
    {
        await using var db = database.CreateContext();
        var person = new Person(Guid.CreateVersion7(), firstName, "Client" + tag, null);
        db.Set<Person>().Add(person);
        db.Set<ClientProfile>().Add(ClientProfile.Create(person.Id, employee, DateTimeOffset.UtcNow));
        var user = User.Create(Guid.CreateVersion7(), person.Id, firstName.ToLowerInvariant() + tag, null, "it", [TenantRole.Client], isActive: true).Value;
        db.Set<User>().Add(user);
        await db.SaveChangesAsync(Ct);
        return (person.Id, user.Id);
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

    private async Task AddSpecializationAsync(string name, bool isPrivate, IReadOnlyCollection<Guid> members)
    {
        await using var db = database.CreateContext();
        var specialization = Specialization.Create(Guid.CreateVersion7(), TenantRole.Client, new SpecializationSpec(name, null, null, null, isPrivate)).Value;
        specialization.AddMembers(members, DateTimeOffset.UtcNow);
        db.Set<Specialization>().Add(specialization);
        await db.SaveChangesAsync(Ct);
    }

    private async Task AddDocumentAsync(Guid clientId)
    {
        await using var db = database.CreateContext();
        var id = Guid.CreateVersion7();
        var upload = new DocumentUpload(clientId, null, null, "file.pdf", "tenants/test/documents/" + id, "application/pdf", 10, new string('a', 64),
            DateTime.UtcNow.Year, null, null, "{}");
        db.Set<Document>().Add(Document.Upload(id, upload, null, DateTimeOffset.UtcNow).Value);
        await db.SaveChangesAsync(Ct);
    }
}
