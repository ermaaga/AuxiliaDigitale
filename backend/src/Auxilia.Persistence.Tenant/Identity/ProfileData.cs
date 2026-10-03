using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Identity;

internal sealed class ProfileDataFactory(ITenantDbContextFactory databases) : IProfileDataFactory
{
    public async Task<IProfileData> OpenAsync(CancellationToken cancellationToken) =>
        new ProfileData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="IProfileData"/>
internal sealed class ProfileData(ITenantDbContext db) : IProfileData
{
    public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<User>().SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public Task<Person?> FindPersonAsync(Guid personId, CancellationToken cancellationToken) =>
        db.Set<Person>().SingleOrDefaultAsync(person => person.Id == personId, cancellationToken);

    public Task<UserImage?> FindImageAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<UserImage>().SingleOrDefaultAsync(image => image.Id == userId, cancellationToken);

    public Task<UserImageInfo?> FindImageInfoAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<UserImage>()
            .AsNoTracking()
            .Where(image => image.Id == userId)
            .Select(image => new UserImageInfo(image.Id, image.Hash))
            .SingleOrDefaultAsync(cancellationToken);

    public void Add(UserImage image) => db.Set<UserImage>().Add(image);

    public void Remove(UserImage image) => db.Set<UserImage>().Remove(image);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();
}
