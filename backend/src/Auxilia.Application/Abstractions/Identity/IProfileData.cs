using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;

namespace Auxilia.Application.Abstractions.Identity;

/// <summary>The image of a user without its bytes (lists and the profile only need the version).</summary>
public sealed record UserImageInfo(Guid UserId, string Hash);

/// <summary>
/// The own profile of a user (F04): the account, its person (not deleted) and the profile picture. One unit of work;
/// inside a write operation it joins the operation's transaction.
/// </summary>
public interface IProfileData : IAsyncDisposable
{
    /// <summary>The account (tracked).</summary>
    Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The person of an account (tracked); <c>null</c> when it is deleted.</summary>
    Task<Person?> FindPersonAsync(Guid personId, CancellationToken cancellationToken);

    /// <summary>The picture with its bytes (tracked).</summary>
    Task<UserImage?> FindImageAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The picture version (hash) without the bytes.</summary>
    Task<UserImageInfo?> FindImageInfoAsync(Guid userId, CancellationToken cancellationToken);

    void Add(UserImage image);

    void Remove(UserImage image);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IProfileDataFactory
{
    Task<IProfileData> OpenAsync(CancellationToken cancellationToken);
}
