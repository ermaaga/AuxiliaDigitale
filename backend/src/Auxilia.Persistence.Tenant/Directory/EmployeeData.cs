using Auxilia.Application.Abstractions.Directory;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.Persistence.Tenant.Conventions;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Directory;

internal sealed class EmployeeDataFactory(ITenantDbContextFactory databases) : IEmployeeDataFactory
{
    public async Task<IEmployeeData> OpenAsync(CancellationToken cancellationToken) =>
        new EmployeeData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="IEmployeeData"/>
internal sealed class EmployeeData(ITenantDbContext db) : IEmployeeData
{
    public async Task<(IReadOnlyList<EmployeeRow> Items, int Total)> PageAsync(EmployeeFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        // People are joined (not left-joined) so their soft-delete filter applies: a deleted employee never appears.
        var employees =
            from user in Employees().AsNoTracking()
            join person in db.Set<Person>() on user.PersonId equals person.Id
            select new { user, person };

        if (filter.CanSignIn is { } canSignIn)
        {
            employees = employees.Where(employee => employee.user.IsActive == canSignIn);
        }

        if (Pattern(filter.FullName) is { } fullName)
        {
            employees = employees.Where(employee => EF.Functions.ILike(employee.person.FirstName + " " + employee.person.LastName, fullName, "\\")
                || EF.Functions.ILike(employee.person.LastName + " " + employee.person.FirstName, fullName, "\\"));
        }

        if (Pattern(filter.LastName) is { } lastName)
        {
            employees = employees.Where(employee => EF.Functions.ILike(employee.person.LastName, lastName, "\\"));
        }

        if (Pattern(filter.Email) is { } email)
        {
            employees = employees.Where(employee => employee.person.Email != null && EF.Functions.ILike(employee.person.Email, email, "\\"));
        }

        if (Pattern(filter.UserName) is { } userName)
        {
            employees = employees.Where(employee => EF.Functions.ILike(employee.user.UserName, userName, "\\"));
        }

        if (Pattern(filter.Phone) is { } phone)
        {
            employees = employees.Where(employee => employee.person.Phone != null && EF.Functions.ILike(employee.person.Phone, phone, "\\"));
        }

        var total = await employees.CountAsync(cancellationToken);
        var sorted = (filter.Sort, filter.Descending) switch
        {
            (EmployeeSort.FullName, false) => employees.OrderBy(employee => employee.person.FirstName).ThenBy(employee => employee.person.LastName),
            (EmployeeSort.FullName, true) => employees.OrderByDescending(employee => employee.person.FirstName).ThenByDescending(employee => employee.person.LastName),
            (EmployeeSort.Email, false) => employees.OrderBy(employee => employee.person.Email),
            (EmployeeSort.Email, true) => employees.OrderByDescending(employee => employee.person.Email),
            (EmployeeSort.UserName, false) => employees.OrderBy(employee => employee.user.UserName),
            (EmployeeSort.UserName, true) => employees.OrderByDescending(employee => employee.user.UserName),
            (_, false) => employees.OrderBy(employee => employee.person.LastName).ThenBy(employee => employee.person.FirstName),
            (_, true) => employees.OrderByDescending(employee => employee.person.LastName).ThenByDescending(employee => employee.person.FirstName),
        };

        var profiles = db.Set<EmployeeProfile>();
        var clients = ClientsInCharge();
        var images = db.Set<UserImage>();
        var items = await sorted
            .ThenBy(employee => employee.user.Id)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .Select(employee => new EmployeeRow(
                employee.user.Id,
                employee.person.FirstName,
                employee.person.LastName,
                employee.person.Email,
                employee.user.UserName,
                employee.person.Phone,
                employee.user.IsActive,
                profiles.Any(profile => profile.Id == employee.user.Id && profile.IsDefault),
                clients.Count(client => client.EmployeeUserId == employee.user.Id),
                images.Where(image => image.Id == employee.user.Id).Select(image => image.Hash).FirstOrDefault()))
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<IReadOnlyList<EmployeeSpecializationRow>> SpecializationsOfAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        userIds.Count == 0
            ? []
            : await (
                    from specialization in db.Set<Specialization>().AsNoTracking()
                    where specialization.IsActive && specialization.Role == TenantRole.Employee
                    from member in specialization.Members
                    where userIds.Contains(member.UserId)
                    orderby specialization.Name
                    select new EmployeeSpecializationRow(member.UserId, specialization.Id, specialization.Name))
                .ToListAsync(cancellationToken);

    public Task<Person?> FindPersonAsync(Guid userId, CancellationToken cancellationToken)
    {
        var employee = Employees().Where(user => user.Id == userId).Select(user => user.PersonId);
        return db.Set<Person>().SingleOrDefaultAsync(person => employee.Contains(person.Id), cancellationToken);
    }

    public Task<EmployeeProfile?> FindProfileAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<EmployeeProfile>().SingleOrDefaultAsync(profile => profile.Id == userId, cancellationToken);

    public Task<EmployeeProfile?> FindDefaultAsync(CancellationToken cancellationToken) =>
        db.Set<EmployeeProfile>().SingleOrDefaultAsync(profile => profile.IsDefault, cancellationToken);

    public async Task<DateTimeOffset?> CreatedAtAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Set<User>()
            .Where(user => user.Id == userId)
            .Select(user => (DateTimeOffset?)EF.Property<DateTimeOffset>(user, TenantConventions.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<string?> ImageVersionAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<UserImage>()
            .AsNoTracking()
            .Where(image => image.Id == userId)
            .Select(image => image.Hash)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ClientProfile>> ClientsInChargeAsync(Guid userId, CancellationToken cancellationToken) =>
        await ClientsInCharge().Where(profile => profile.EmployeeUserId == userId).ToListAsync(cancellationToken);

    public Task<int> CountClientsInChargeAsync(Guid userId, CancellationToken cancellationToken) =>
        ClientsInCharge().CountAsync(profile => profile.EmployeeUserId == userId, cancellationToken);

    public Task<bool> FiscalCodeTakenAsync(string fiscalCode, Guid? exceptPersonId, CancellationToken cancellationToken) =>
        db.Set<Person>().AnyAsync(person => person.FiscalCode == fiscalCode && person.Id != exceptPersonId, cancellationToken);

    public async Task<IReadOnlyList<EmployeeName>> AdministratorsAsync(CancellationToken cancellationToken) =>
        await (
                from user in WithRole(TenantRole.Administrator).Where(user => user.IsActive)
                join person in db.Set<Person>() on user.PersonId equals person.Id
                orderby person.LastName, person.FirstName, user.Id
                select new EmployeeName(user.Id, person.FirstName + " " + person.LastName))
            .ToListAsync(cancellationToken);

    /// <summary>Deleted people included: an administrator deleted later still has a name.</summary>
    public async Task<IReadOnlyList<EmployeeName>> NamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        userIds.Count == 0
            ? []
            : await (
                    from user in db.Set<User>()
                    where userIds.Contains(user.Id)
                    join person in db.Set<Person>().IgnoreQueryFilters() on user.PersonId equals person.Id
                    select new EmployeeName(user.Id, person.FirstName + " " + person.LastName))
                .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Specialization>> EmployeeSpecializationsAsync(CancellationToken cancellationToken) =>
        await db.Set<Specialization>()
            .Where(specialization => specialization.IsActive && specialization.Role == TenantRole.Employee)
            .OrderBy(specialization => specialization.Name)
            .ToListAsync(cancellationToken);

    public void Add(Person person) => db.Set<Person>().Add(person);

    public void Add(EmployeeProfile profile) => db.Set<EmployeeProfile>().Add(profile);

    public void Remove(Person person) => db.Set<Person>().Remove(person);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();

    private IQueryable<User> Employees() => WithRole(TenantRole.Employee);

    private IQueryable<User> WithRole(TenantRole role) =>
        db.Set<User>().Where(user => EF.Property<List<UserRole>>(user, "roles").Any(item => item.Role == role));

    /// <summary>Client profiles whose person is not deleted (the person's soft-delete filter decides).</summary>
    private IQueryable<ClientProfile> ClientsInCharge()
    {
        var people = db.Set<Person>().Select(person => person.Id);
        return db.Set<ClientProfile>().Where(profile => profile.EmployeeUserId != null && people.Contains(profile.Id));
    }

    /// <summary>A "contains" ILIKE pattern with the wildcards of the text escaped; <c>null</c> for no filter.</summary>
    private static string? Pattern(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : "%" + text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
}
