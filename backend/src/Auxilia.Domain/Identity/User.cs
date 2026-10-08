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

/// <summary>The colour scheme a user chose for the web app (Q35: kept with the account, not in the session).</summary>
public enum UserTheme
{
    /// <summary>Follows the device setting.</summary>
    System,

    Light,

    Dark,
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

    /// <summary>Password hashes kept for the history rule (the setting <c>auth.password.historyCount</c> is at most this).</summary>
    public const int MaxPasswordHistory = 24;

    private readonly List<UserRole> roles = [];
    private readonly List<PasswordHistoryEntry> passwordHistory = [];

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

    /// <summary>Q35: the colour scheme of the web app, persisted with the account.</summary>
    public UserTheme Theme { get; private set; }

    /// <summary>Can sign in (D-05); a disabled account keeps its data and roles.</summary>
    public bool IsActive { get; private set; }

    /// <summary><c>null</c> until the account is activated (D-06).</summary>
    public string? PasswordHash { get; private set; }

    public PasswordFormat PasswordFormat { get; private set; }

    public DateTimeOffset? PasswordChangedAt { get; private set; }

    /// <summary>The password was set by an operator (auxctl, F31): it must be changed before signing in.</summary>
    public bool MustChangePassword { get; private set; }

    public string SecurityStamp { get; private set; }

    public int AccessFailedCount { get; private set; }

    /// <summary>How many times the account was locked since the last successful sign-in (progressive duration).</summary>
    public int LockoutCount { get; private set; }

    public DateTimeOffset? LockoutEnd { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    /// <summary>The confirmed TOTP secret (protected with Data Protection), or null without the authenticator app (N04).</summary>
    public string? TwoFactorSecret { get; private set; }

    /// <summary>A secret being enrolled, confirmed by <see cref="ConfirmTwoFactor"/>.</summary>
    public string? PendingTwoFactorSecret { get; private set; }

    /// <summary>The last TOTP step accepted: a code of that step or an earlier one is refused (replay).</summary>
    public long? LastTotpStep { get; private set; }

    public DateTimeOffset? TwoFactorEnabledAt { get; private set; }

    public bool HasTwoFactor => TwoFactorSecret is not null;

    public IReadOnlyCollection<TenantRole> Roles => roles.Select(role => role.Role).Order().ToArray();

    /// <summary>Hashes of the passwords set so far, newest first (the current one included), at most <see cref="MaxPasswordHistory"/> (F35).</summary>
    public IReadOnlyList<PasswordHistoryEntry> PasswordHistory => passwordHistory.OrderByDescending(entry => entry.CreatedAt).ToArray();

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
        MustChangePassword = false;
        AccessFailedCount = 0;
        LockoutCount = 0;
        LockoutEnd = null;
        SecurityStamp = NewStamp();

        passwordHistory.Add(new PasswordHistoryEntry(Guid.CreateVersion7(), Id, passwordHash, format, at));
        foreach (var old in passwordHistory.OrderByDescending(entry => entry.CreatedAt).Skip(MaxPasswordHistory).ToArray())
        {
            passwordHistory.Remove(old);
        }
    }

    /// <summary>
    /// A temporary password set by an operator (F31): like <see cref="SetPassword"/>, but the user must replace it before
    /// signing in (same flow as an expired password).
    /// </summary>
    public void SetTemporaryPassword(string passwordHash, PasswordFormat format, DateTimeOffset at)
    {
        SetPassword(passwordHash, format, at);
        MustChangePassword = true;
    }

    /// <summary>
    /// Legacy import (E-02): the BCrypt hash the user had in the legacy application, set at <paramref name="changedAt"/>
    /// and verified (then rehashed) at the first sign-in. <paramref name="mustChange"/> marks hashes of passwords the
    /// legacy application assigned itself (seed, approval of a registration). A different hash invalidates older sessions.
    /// </summary>
    public void ImportLegacyPassword(string passwordHash, DateTimeOffset changedAt, bool mustChange)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        if (PasswordHash != passwordHash)
        {
            SecurityStamp = NewStamp();
        }

        PasswordHash = passwordHash;
        PasswordFormat = PasswordFormat.LegacyBcrypt;
        PasswordChangedAt = changedAt;
        MustChangePassword = mustChange;
        ImportLegacyPasswordHistory(passwordHash, changedAt);
    }

    /// <summary>Legacy import (E-02, F35): a previous BCrypt hash of the user; returns whether it was not known yet.</summary>
    public bool ImportLegacyPasswordHistory(string passwordHash, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        if (passwordHistory.Any(entry => entry.PasswordHash == passwordHash))
        {
            return false;
        }

        passwordHistory.Add(new PasswordHistoryEntry(Guid.CreateVersion7(), Id, passwordHash, PasswordFormat.LegacyBcrypt, createdAt));
        foreach (var old in passwordHistory.OrderByDescending(entry => entry.CreatedAt).Skip(MaxPasswordHistory).ToArray())
        {
            passwordHistory.Remove(old);
        }

        return true;
    }

    /// <summary>F35: with expiry enabled, a password older than <paramref name="maxAge"/> must be changed before signing in.</summary>
    public bool IsPasswordExpired(DateTimeOffset now, TimeSpan maxAge) =>
        PasswordHash is not null && (PasswordChangedAt is not { } changed || changed + maxAge <= now);

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

    /// <summary>
    /// Changes user name and e-mail (staff edit of a client, Q52); uniqueness of the user name is checked by the caller.
    /// The security stamp changes with the user name, so tokens issued under the old name stop working.
    /// </summary>
    public Result ChangeAccount(string userName, string? email)
    {
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

        if (!string.Equals(UserName, name, StringComparison.OrdinalIgnoreCase))
        {
            SecurityStamp = NewStamp();
        }

        UserName = name;
        Email = mail;
        return Result.Success();
    }

    /// <summary>The language of the user (F04); the caller checks it is an active language of the tenant.</summary>
    public Result ChangeLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode) || languageCode.Length > LanguageMaxLength)
        {
            return Errors.Identity.UserValueInvalid("languageCode");
        }

        LanguageCode = languageCode;
        return Result.Success();
    }

    public Result ChangeTheme(UserTheme theme)
    {
        if (!Enum.IsDefined(theme))
        {
            return Errors.Identity.UserValueInvalid("theme");
        }

        Theme = theme;
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

    /// <summary>Starts (or restarts) the enrolment of the authenticator app with a new protected secret.</summary>
    public void BeginTwoFactorEnrollment(string protectedSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedSecret);
        PendingTwoFactorSecret = protectedSecret;
    }

    /// <summary>The pending secret becomes the active one, after a code of the app was verified at <paramref name="totpStep"/>.</summary>
    public Result ConfirmTwoFactor(long totpStep, DateTimeOffset now)
    {
        if (PendingTwoFactorSecret is null)
        {
            return Errors.Identity.TwoFactorEnrollmentMissing();
        }

        TwoFactorSecret = PendingTwoFactorSecret;
        PendingTwoFactorSecret = null;
        LastTotpStep = totpStep;
        TwoFactorEnabledAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Removes the authenticator app: by the user (disable) or by an Administrator / the platform (reset, lost phone). A
    /// reset also ends the user's sessions (new security stamp).
    /// </summary>
    public void RemoveTwoFactor(bool endSessions)
    {
        TwoFactorSecret = null;
        PendingTwoFactorSecret = null;
        LastTotpStep = null;
        TwoFactorEnabledAt = null;
        if (endSessions)
        {
            SecurityStamp = NewStamp();
        }
    }

    /// <summary>Records the TOTP step of an accepted code; false when that step (or a later one) was already used.</summary>
    public bool TryUseTotpStep(long step)
    {
        if (LastTotpStep is { } last && step <= last)
        {
            return false;
        }

        LastTotpStep = step;
        return true;
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

/// <summary>A password hash the user had (<c>identity.password_history</c>): a new password may not match the last N (F35).</summary>
public sealed class PasswordHistoryEntry
{
    internal PasswordHistoryEntry(Guid id, Guid userId, string passwordHash, PasswordFormat format, DateTimeOffset createdAt)
    {
        Id = id;
        UserId = userId;
        PasswordHash = passwordHash;
        Format = format;
        CreatedAt = createdAt;
    }

    private PasswordHistoryEntry()
    {
        PasswordHash = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string PasswordHash { get; private set; }

    public PasswordFormat Format { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
