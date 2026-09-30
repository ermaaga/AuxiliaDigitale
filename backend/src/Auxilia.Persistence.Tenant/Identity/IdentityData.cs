using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Identity;

internal sealed class IdentityDataFactory(ITenantDbContextFactory databases) : IIdentityDataFactory
{
    public async Task<IIdentityData> OpenAsync(CancellationToken cancellationToken) =>
        new IdentityData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="IIdentityData"/>
internal sealed class IdentityData(ITenantDbContext db) : IIdentityData
{
    // user_name is citext: equality is case-insensitive in the database.
    public Task<User?> FindByUserNameAsync(string userName, CancellationToken cancellationToken) =>
        db.Set<User>().SingleOrDefaultAsync(user => user.UserName == userName, cancellationToken);

    public Task<User?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<User>().SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public Task<bool> UserNameExistsAsync(string userName, CancellationToken cancellationToken) =>
        db.Set<User>().AnyAsync(user => user.UserName == userName, cancellationToken);

    public Task<bool> PersonExistsAsync(Guid personId, CancellationToken cancellationToken) =>
        db.Set<Person>().AnyAsync(person => person.Id == personId, cancellationToken);

    public void Add(User user) => db.Set<User>().Add(user);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();
}
