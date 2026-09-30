using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Domain.Identity;

/// <summary>How <see cref="User.PasswordHash"/> was produced.</summary>
public enum PasswordFormat
{
    /// <summary>ASP.NET Identity hasher (PBKDF2).</summary>
    Identity,

    /// <summary>BCrypt hash imported from the legacy application: verified once, then rehashed (F01).</summary>
    LegacyBcrypt,
}

/// <summary>
/// A tenant user account (<c>identity.users</c>). Sign-in access (<see cref="IsActive"/>) is independent from the
/// client's business status (D-05). New accounts have no password until activation (D-06). The user name is unique
/// and case-insensitive (F01). Failed sign-ins lock the account progressively; <see cref="SecurityStamp"/> changes with
/// password and roles, so sessions issued before can be invalidated (P2-02).
/// </summary>
public sealed class User : AggregateRoot<Guid>, IAuditable
{
    public const int UserNameMaxLength = 256;
    public const int EmailMaxLength = 256;
    public const int LanguageMaxLength = 10;
    public const int SecurityStampLength = 32;

    /// <summary>Upper bound of a progressive lockout.</summary>
    public static readonly TimeSpan MaxLockout = TimeSpan.FromHours(24);

    private readonly List<UserRole> roles = [];

    private User(Guid id, Guid personId, string userName, string? email, string languageCode, bool isActive)
        : base(id)
    {
        PersonId = personId;
        UserName = userName;
        Email = email;
        LanguageCode = languageCode;
        IsActive = isActive;
        SecurityStamp = NewStamp();
    }

    private User()
    {
        UserName = LanguageCode = SecurityStamp = string.Empty;
    }

    public Guid PersonId { get; private set; }

    public string UserName { get; private set; }

    public string? Email { get; private set; }

    public string LanguageCode { get; private set; }

    /// <summary>Can sign in (D-05); a disabled account keeps its data and roles.</summary>
    public bool IsActive { get; private set; }

    /// <summary><c>null</c> until the account is activated (D-06).</summary>
    public string? PasswordHash { get; private set; }

    public PasswordFormat PasswordFormat { get; private set; }

    public DateTimeOffset? PasswordChangedAt { get; private set; }

    public string SecurityStamp { get; private set; }

    public int AccessFailedCount { get; private set; }

    /// <summary>How many times the account was locked since the last successful sign-in (progressive duration).</summary>
    public int LockoutCount { get; private set; }

    public DateTimeOffset? LockoutEnd { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public IReadOnlyCollection<TenantRole> Roles => roles.Select(role => role.Role).Order().ToArray();

    public static Result<User> Create(Guid id, Guid personId, string userName, string? email, string languageCode, IReadOnlyCollection<TenantRole> roles, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var name = userName?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > UserNameMaxLength || name.Any(char.IsWhiteSpace))
        {
            return Errors.Identity.UserValueInvalid("userName");
        }

        var mail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (mail is not null && (mail.Length > EmailMaxLength || !mail.Contains('@', StringComparison.Ordinal)))
        {
            return Errors.Identity.UserValueInvalid("email");
        }

        if (string.IsNullOrWhiteSpace(languageCode) || languageCode.Length > LanguageMaxLength)
        {
            return Errors.Identity.UserValueInvalid("languageCode");
        }

        if (roles.Count == 0 || roles.Any(role => !Enum.IsDefined(role)))
        {
            return Errors.Identity.UserValueInvalid("roles");
        }

        var user = new User(id, personId, name, mail, languageCode, isActive);
        user.roles.AddRange(roles.Distinct().Select(role => new UserRole(id, role)));
        return user;
    }

    /// <summary>A password hashed by the caller; a new password unlocks the account and invalidates older sessions.</summary>
    public void SetPassword(string passwordHash, PasswordFormat format, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        PasswordHash = passwordHash;
        PasswordFormat = format;
        PasswordChangedAt = at;
        AccessFailedCount = 0;
        LockoutCount = 0;
        LockoutEnd = null;
        SecurityStamp = NewStamp();
    }

    /// <summary>Replaces the hash after a successful verification with an outdated format (no new stamp: same password).</summary>
    public void UpgradePasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        PasswordHash = passwordHash;
        PasswordFormat = PasswordFormat.Identity;
    }

    public Result SetRoles(IReadOnlyCollection<TenantRole> newRoles)
    {
        ArgumentNullException.ThrowIfNull(newRoles);

        if (newRoles.Count == 0 || newRoles.Any(role => !Enum.IsDefined(role)))
        {
            return Errors.Identity.UserValueInvalid("roles");
        }

        roles.RemoveAll(role => !newRoles.Contains(role.Role));
        foreach (var role in newRoles.Distinct().Where(role => roles.All(existing => existing.Role != role)))
        {
            roles.Add(new UserRole(Id, role));
        }

        SecurityStamp = NewStamp();
        return Result.Success();
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        if (!isActive)
        {
            SecurityStamp = NewStamp();
        }
    }

    public bool IsLockedOut(DateTimeOffset now) => LockoutEnd is { } end && end > now;

    /// <summary>
    /// Counts a failed sign-in; at <paramref name="maxFailedAttempts"/> the account is locked for
    /// <paramref name="lockoutDuration"/> doubled for each previous lockout (at most <see cref="MaxLockout"/>).
    /// </summary>
    /// <returns>The end of the lockout when this attempt locked the account.</returns>
    public DateTimeOffset? RecordFailedSignIn(DateTimeOffset now, int maxFailedAttempts, TimeSpan lockoutDuration)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFailedAttempts);

        AccessFailedCount++;
        if (AccessFailedCount < maxFailedAttempts)
        {
            return null;
        }

        var factor = Math.Pow(2, Math.Min(LockoutCount, 10));
        var duration = TimeSpan.FromTicks((long)Math.Min(lockoutDuration.Ticks * factor, MaxLockout.Ticks));
        LockoutCount++;
        AccessFailedCount = 0;
        LockoutEnd = now + duration;
        return LockoutEnd;
    }

    public void RecordSuccessfulSignIn(DateTimeOffset now)
    {
        AccessFailedCount = 0;
        LockoutCount = 0;
        LockoutEnd = null;
        LastLoginAt = now;
    }

    private static string NewStamp() => Guid.NewGuid().ToString("N");
}

/// <summary>A role of a user (<c>identity.user_roles</c>).</summary>
public sealed class UserRole
{
    internal UserRole(Guid userId, TenantRole role)
    {
        UserId = userId;
        Role = role;
    }

    private UserRole()
    {
    }

    public Guid UserId { get; private set; }

    public TenantRole Role { get; private set; }
}
