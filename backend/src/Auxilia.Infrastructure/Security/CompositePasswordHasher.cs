using Auxilia.Domain.Identity;

using Microsoft.AspNetCore.Identity;

using PasswordVerification = Auxilia.Application.Abstractions.Identity.PasswordVerification;

namespace Auxilia.Infrastructure.Security;

/// <summary>
/// ASP.NET Identity <see cref="PasswordHasher{TUser}"/> (PBKDF2, V3 format) for every new hash; legacy BCrypt hashes
/// (imported from the legacy application) are verified with BCrypt and always reported as needing a rehash (F01).
/// </summary>
internal sealed class CompositePasswordHasher : Application.Abstractions.Identity.IPasswordHasher
{
    private static readonly object User = new();

    private readonly PasswordHasher<object> identity = new();

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return identity.HashPassword(User, password);
    }

    public PasswordVerification Verify(string passwordHash, PasswordFormat format, string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(passwordHash);
        ArgumentNullException.ThrowIfNull(password);

        if (format == PasswordFormat.LegacyBcrypt)
        {
            return VerifyBcrypt(passwordHash, password) ? PasswordVerification.SuccessRehashNeeded : PasswordVerification.Failed;
        }

        try
        {
            return identity.VerifyHashedPassword(User, passwordHash, password) switch
            {
                PasswordVerificationResult.Success => PasswordVerification.Success,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordVerification.SuccessRehashNeeded,
                _ => PasswordVerification.Failed,
            };
        }
        catch (FormatException)
        {
            return PasswordVerification.Failed;
        }
    }

    private static bool VerifyBcrypt(string hash, string password)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}
