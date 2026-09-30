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

    void Add(User user);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IIdentityDataFactory
{
    Task<IIdentityData> OpenAsync(CancellationToken cancellationToken);
}
