using Auxilia.Domain.Identity;

namespace Auxilia.Application.Abstractions.Identity;

/// <summary>Result of a password verification (ASP.NET Identity semantics).</summary>
public enum PasswordVerification
{
    Failed,
    Success,

    /// <summary>Correct, but stored in an outdated format or with old parameters: rehash it.</summary>
    SuccessRehashNeeded,
}

/// <summary>
/// Password hashing (skill auxilia-security): ASP.NET Identity hasher (PBKDF2) for new hashes; legacy BCrypt hashes
/// (<see cref="PasswordFormat.LegacyBcrypt"/>) are verified and reported as needing a rehash.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    PasswordVerification Verify(string passwordHash, PasswordFormat format, string password);
}

/// <summary>Identity data of the current tenant, one unit of work (joins the running operation's transaction).</summary>
public interface IIdentityData : IAsyncDisposable
{
    /// <summary>Case-insensitive match (F01).</summary>
    Task<User?> FindByUserNameAsync(string userName, CancellationToken cancellationToken);

    Task<User?> FindAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> UserNameExistsAsync(string userName, CancellationToken cancellationToken);

    Task<bool> PersonExistsAsync(Guid personId, CancellationToken cancellationToken);

    /// <summary>The account of the person (tracked), if any.</summary>
    Task<User?> FindByPersonAsync(Guid personId, CancellationToken cancellationToken);

    /// <summary>The accounts among <paramref name="userIds"/> that exist (read-only).</summary>
    Task<IReadOnlyList<User>> FindManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>Users that have the role (tracked), by user name.</summary>
    Task<IReadOnlyList<User>> UsersWithRoleAsync(Auxilia.SharedKernel.Tenancy.TenantRole role, CancellationToken cancellationToken);

    void Add(User user);

    /// <summary>A person of the Directory (only the first Administrator is created this way; clients and employees come with B-01/B-02).</summary>
    void Add(Auxilia.Domain.Directory.Person person);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IIdentityDataFactory
{
    Task<IIdentityData> OpenAsync(CancellationToken cancellationToken);
}
