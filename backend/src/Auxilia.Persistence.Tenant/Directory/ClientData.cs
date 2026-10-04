using Auxilia.Application.Abstractions.Directory;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Directory;

internal sealed class ClientDataFactory(ITenantDbContextFactory databases) : IClientDataFactory
{
    public async Task<IClientData> OpenAsync(CancellationToken cancellationToken) =>
        new ClientData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="IClientData"/>
internal sealed class ClientData(ITenantDbContext db) : IClientData
{

    public Task<int> CountFlaggedAsync(string key, Guid? employeeUserId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // Profiles are joined to people (soft-delete filter: deleted clients are not counted); the flag is a JSON true.
        var contained = $"{{{System.Text.Json.JsonSerializer.Serialize(key)}: true}}";
        var flagged =
            from profile in db.Set<ClientProfile>().AsNoTracking()
            join person in db.Set<Person>() on profile.Id equals person.Id
            where EF.Functions.JsonContains(person.CustomFields, contained)
            select profile;
        if (employeeUserId is { } employee)
        {
            flagged = flagged.Where(profile => profile.EmployeeUserId == employee);
        }

        return flagged.CountAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<ClientRow> Items, int Total)> PageAsync(ClientFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        // People are the root so their soft-delete filter applies: a deleted client never appears.
        var clients =
            from person in db.Set<Person>().AsNoTracking()
            join profile in db.Set<ClientProfile>() on person.Id equals profile.Id
            join user in db.Set<User>() on person.Id equals user.PersonId
            select new { profile, person, user };

        if (filter.EmployeeUserId is { } employeeUserId)
        {
            clients = clients.Where(client => client.profile.EmployeeUserId == employeeUserId);
        }

        if (filter.Status is { } status)
        {
            clients = clients.Where(client => client.profile.Status == status);
        }

        if (Pattern(filter.FullName) is { } fullName)
        {
            clients = clients.Where(client => EF.Functions.ILike(client.person.FirstName + " " + client.person.LastName, fullName, "\\")
                || EF.Functions.ILike(client.person.LastName + " " + client.person.FirstName, fullName, "\\"));
        }

        if (Pattern(filter.LastName) is { } lastName)
        {
            clients = clients.Where(client => EF.Functions.ILike(client.person.LastName, lastName, "\\"));
        }

        if (Pattern(filter.Email) is { } email)
        {
            clients = clients.Where(client => client.person.Email != null && EF.Functions.ILike(client.person.Email, email, "\\"));
        }

        if (Pattern(filter.UserName) is { } userName)
        {
            clients = clients.Where(client => EF.Functions.ILike(client.user.UserName, userName, "\\"));
        }

        if (Pattern(filter.Phone) is { } phone)
        {
            clients = clients.Where(client => client.person.Phone != null && EF.Functions.ILike(client.person.Phone, phone, "\\"));
        }

        var total = await clients.CountAsync(cancellationToken);
        var sorted = (filter.Sort, filter.Descending) switch
        {
            (ClientSort.FullName, false) => clients.OrderBy(client => client.person.FirstName).ThenBy(client => client.person.LastName),
            (ClientSort.FullName, true) => clients.OrderByDescending(client => client.person.FirstName).ThenByDescending(client => client.person.LastName),
            (ClientSort.Email, false) => clients.OrderBy(client => client.person.Email),
            (ClientSort.Email, true) => clients.OrderByDescending(client => client.person.Email),
            (ClientSort.UserName, false) => clients.OrderBy(client => client.user.UserName),
            (ClientSort.UserName, true) => clients.OrderByDescending(client => client.user.UserName),
            (_, false) => clients.OrderBy(client => client.person.LastName).ThenBy(client => client.person.FirstName),
            (_, true) => clients.OrderByDescending(client => client.person.LastName).ThenByDescending(client => client.person.FirstName),
        };

        // No IgnoreQueryFilters here: in EF Core it applies to the whole query and would show deleted clients.
        var users = db.Set<User>();
        var images = db.Set<UserImage>();
        var names = db.Set<Person>();
        var items = await sorted
            .ThenBy(client => client.person.Id)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .Select(client => new ClientRow(
                client.person.Id,
                client.person.FirstName,
                client.person.LastName,
                client.person.Email,
                client.user.UserName,
                client.person.Phone,
                client.person.FiscalCode,
                client.profile.Status,
                client.user.IsActive,
                client.profile.EmployeeUserId,
                (from employee in users
                 where employee.Id == client.profile.EmployeeUserId
                 join person in names on employee.PersonId equals person.Id
                 select person.FirstName + " " + person.LastName).FirstOrDefault(),
                client.person.CustomFields,
                client.user.Id,
                images.Where(image => image.Id == client.user.Id).Select(image => image.Hash).FirstOrDefault()))
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<Person?> FindPersonAsync(Guid clientId, CancellationToken cancellationToken) =>
        db.Set<Person>().SingleOrDefaultAsync(
            person => person.Id == clientId && db.Set<ClientProfile>().Any(profile => profile.Id == clientId), cancellationToken);

    public async Task<ClientProfile?> FindProfileAsync(Guid clientId, CancellationToken cancellationToken) =>
        // The person (root query, soft-delete filter) decides whether the client still exists.
        await db.Set<Person>().AnyAsync(person => person.Id == clientId, cancellationToken)
            ? await db.Set<ClientProfile>().SingleOrDefaultAsync(profile => profile.Id == clientId, cancellationToken)
            : null;

    public Task<bool> FiscalCodeTakenAsync(string fiscalCode, Guid? exceptPersonId, CancellationToken cancellationToken) =>
        db.Set<Person>().AnyAsync(person => person.FiscalCode == fiscalCode && person.Id != exceptPersonId, cancellationToken);

    /// <summary>Deleted people included: the history keeps showing who it was.</summary>
    public async Task<IReadOnlyList<EmployeeName>> NamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        userIds.Count == 0
            ? []
            : await (
                    from user in db.Set<User>()
                    where userIds.Contains(user.Id)
                    join person in db.Set<Person>().IgnoreQueryFilters() on user.PersonId equals person.Id
                    select new EmployeeName(user.Id, person.FirstName + " " + person.LastName))
                .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EmployeeName>> AssignableEmployeesAsync(CancellationToken cancellationToken)
    {
        var employees = db.Set<User>()
            .Where(user => user.IsActive && EF.Property<List<UserRole>>(user, "roles").Any(role => role.Role == TenantRole.Employee));
        return await (
                from user in employees
                join person in db.Set<Person>() on user.PersonId equals person.Id
                orderby person.LastName, person.FirstName, user.Id
                select new EmployeeName(user.Id, person.FirstName + " " + person.LastName))
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid?> DefaultEmployeeAsync(CancellationToken cancellationToken) =>
        await (
                from profile in db.Set<EmployeeProfile>()
                where profile.IsDefault
                join user in db.Set<User>() on profile.Id equals user.Id
                where user.IsActive && EF.Property<List<UserRole>>(user, "roles").Any(role => role.Role == TenantRole.Employee)
                join person in db.Set<Person>() on user.PersonId equals person.Id
                select (Guid?)user.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<string?> ImageVersionAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<UserImage>()
            .AsNoTracking()
            .Where(image => image.Id == userId)
            .Select(image => image.Hash)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Specialization>> ClientSpecializationsAsync(CancellationToken cancellationToken) =>
        await db.Set<Specialization>()
            .Where(specialization => specialization.IsActive && specialization.Role == TenantRole.Client)
            .OrderBy(specialization => specialization.Name)
            .ToListAsync(cancellationToken);

    public void Add(Person person) => db.Set<Person>().Add(person);

    public void Add(ClientProfile profile) => db.Set<ClientProfile>().Add(profile);

    public void Remove(Person person) => db.Set<Person>().Remove(person);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();

    /// <summary>A "contains" ILIKE pattern with the wildcards of the text escaped; <c>null</c> for no filter.</summary>
    private static string? Pattern(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : "%" + text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
}
