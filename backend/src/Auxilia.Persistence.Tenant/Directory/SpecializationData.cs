using Auxilia.Application.Abstractions.Directory;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Directory;

internal sealed class SpecializationDataFactory(ITenantDbContextFactory databases) : ISpecializationDataFactory
{
    public async Task<ISpecializationData> OpenAsync(CancellationToken cancellationToken) =>
        new SpecializationData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="ISpecializationData"/>
internal sealed class SpecializationData(ITenantDbContext db) : ISpecializationData
{
    public async Task<IReadOnlyList<Specialization>> ListAsync(TenantRole? role, CancellationToken cancellationToken) =>
        await db.Set<Specialization>()
            .AsNoTracking()
            .Where(specialization => specialization.IsActive && (role == null || specialization.Role == role))
            .OrderBy(specialization => specialization.Role)
            .ThenBy(specialization => specialization.Name)
            .ToListAsync(cancellationToken);

    public Task<Specialization?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Specialization>().SingleOrDefaultAsync(specialization => specialization.Id == id && specialization.IsActive, cancellationToken);

    // name is citext: equality is case-insensitive in the database.
    public Task<bool> NameTakenAsync(TenantRole role, string name, Guid? exceptId, CancellationToken cancellationToken) =>
        db.Set<Specialization>().AnyAsync(
            specialization => specialization.IsActive && specialization.Role == role && specialization.Name == name && specialization.Id != exceptId,
            cancellationToken);

    public async Task<IReadOnlyList<DirectoryUser>> MembersAsync(Guid id, CancellationToken cancellationToken)
    {
        var memberIds = db.Set<Specialization>().Where(specialization => specialization.Id == id).SelectMany(specialization => specialization.Members).Select(member => member.UserId);
        return await Users(db.Set<User>().Where(user => memberIds.Contains(user.Id)), int.MaxValue, cancellationToken);
    }

    public async Task<IReadOnlyList<DirectoryUser>> CandidatesAsync(Guid id, TenantRole role, string? search, int limit, CancellationToken cancellationToken)
    {
        var memberIds = db.Set<Specialization>().Where(specialization => specialization.Id == id).SelectMany(specialization => specialization.Members).Select(member => member.UserId);
        var users = WithRole(role).Where(user => !memberIds.Contains(user.Id));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            var people = db.Set<Person>()
                .Where(person => EF.Functions.ILike(person.FirstName + " " + person.LastName, pattern, "\\")
                    || EF.Functions.ILike(person.LastName + " " + person.FirstName, pattern, "\\"))
                .Select(person => person.Id);
            users = users.Where(user => EF.Functions.ILike(user.UserName, pattern, "\\")
                || (user.Email != null && EF.Functions.ILike(user.Email, pattern, "\\"))
                || people.Contains(user.PersonId));
        }

        return await Users(users, limit, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> UsersWithRoleAsync(IReadOnlyCollection<Guid> userIds, TenantRole role, CancellationToken cancellationToken) =>
        await WithRole(role).Where(user => userIds.Contains(user.Id)).Select(user => user.Id).ToListAsync(cancellationToken);

    public void Add(Specialization specialization) => db.Set<Specialization>().Add(specialization);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();

    private IQueryable<User> WithRole(TenantRole role) =>
        db.Set<User>().Where(user => EF.Property<List<UserRole>>(user, "roles").Any(item => item.Role == role));

    private async Task<IReadOnlyList<DirectoryUser>> Users(IQueryable<User> users, int limit, CancellationToken cancellationToken)
    {
        var rows = await (
                from user in users
                join person in db.Set<Person>() on user.PersonId equals person.Id into people
                from person in people.DefaultIfEmpty()
                orderby user.UserName
                select new { user.Id, user.UserName, user.Email, user.IsActive, FirstName = person == null ? null : person.FirstName, LastName = person == null ? null : person.LastName })
            .AsNoTracking()
            .Take(limit)
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new DirectoryUser(row.Id, row.UserName, row.FirstName is null ? null : $"{row.FirstName} {row.LastName}", row.Email, row.IsActive))
            .ToArray();
    }
}
