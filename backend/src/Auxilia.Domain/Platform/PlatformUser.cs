using Auxilia.SharedKernel.Domain;

namespace Auxilia.Domain.Platform;

/// <summary>
/// A platform (System console) user (catalog <c>platform_users</c> + <c>platform_user_roles</c>). Signs in with password
/// and a mandatory TOTP code (D-22); both are set at activation (one-use activation token from <c>auxctl</c>), the TOTP
/// secret is stored protected with Data Protection. Progressive lockout as for tenant users.
/// </summary>
public sealed class PlatformUser : AggregateRoot<Guid>
{
    public const int EmailMaxLength = 256;
    public const int DisplayNameMaxLength = 200;
    public const string SystemRole = "System";
    public const int SecurityStampLength = 32;
    public static readonly TimeSpan MaxLockout = TimeSpan.FromHours(24);

    private readonly List<PlatformUserRole> roles = [];

    public PlatformUser(Guid id, string email, string displayName)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(email.Length, EmailMaxLength);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(displayName.Length, DisplayNameMaxLength);

        Email = email.Trim();
        DisplayName = displayName.Trim();
        IsActive = true;
        SecurityStamp = NewStamp();
        roles.Add(new PlatformUserRole(id, SystemRole));
    }

    private PlatformUser()
    {
        Email = DisplayName = SecurityStamp = string.Empty;
    }

    public string Email { get; private set; }

    public string DisplayName { get; private set; }

    /// <summary>Null until the user completes activation.</summary>
    public string? PasswordHash { get; private set; }

    /// <summary>TOTP secret protected with Data Protection; null until 2FA is enrolled.</summary>
    public string? TwoFactorSecret { get; private set; }

    /// <summary>Protected TOTP secret shown at enrolment and not yet confirmed with a code.</summary>
    public string? PendingTwoFactorSecret { get; private set; }

    /// <summary>Last accepted TOTP time step: a code is accepted once (no replay within its window).</summary>
    public long? LastTotpStep { get; private set; }

    /// <summary>Changes with password, 2FA reset and deactivation: sessions opened before end at their next refresh.</summary>
    public string SecurityStamp { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>Password and TOTP are set: the user can sign in.</summary>
    public bool IsEnrolled => PasswordHash is not null && TwoFactorSecret is not null;

    public int AccessFailedCount { get; private set; }

    public int LockoutCount { get; private set; }

    public DateTimeOffset? LockoutEnd { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public IReadOnlyCollection<PlatformUserRole> Roles => roles.AsReadOnly();

    public void Deactivate()
    {
        IsActive = false;
        SecurityStamp = NewStamp();
    }

    public void Activate() => IsActive = true;

    /// <summary>Starts (or restarts) the TOTP enrolment with a new protected secret, confirmed by <see cref="CompleteEnrollment"/>.</summary>
    public void BeginEnrollment(string protectedSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedSecret);
        PendingTwoFactorSecret = protectedSecret;
    }

    /// <summary>Password and the confirmed TOTP secret become active; older sessions end, the lockout is cleared.</summary>
    public void CompleteEnrollment(string passwordHash, long totpStep)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        if (PendingTwoFactorSecret is null)
        {
            throw new InvalidOperationException("No TOTP enrolment is pending.");
        }

        PasswordHash = passwordHash;
        TwoFactorSecret = PendingTwoFactorSecret;
        PendingTwoFactorSecret = null;
        LastTotpStep = totpStep;
        AccessFailedCount = 0;
        LockoutCount = 0;
        LockoutEnd = null;
        SecurityStamp = NewStamp();
    }

    /// <summary>Recovery (<c>auxctl platform users reset</c>): password and TOTP must be set again with a new activation token.</summary>
    public void ResetCredentials()
    {
        PasswordHash = null;
        TwoFactorSecret = null;
        PendingTwoFactorSecret = null;
        LastTotpStep = null;
        SecurityStamp = NewStamp();
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

    public bool IsLockedOut(DateTimeOffset now) => LockoutEnd is { } end && end > now;

    /// <summary>Same progressive lockout as tenant users (doubled for each previous lockout, at most <see cref="MaxLockout"/>).</summary>
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

public sealed class PlatformUserRole
{
    public const int RoleMaxLength = 50;

    internal PlatformUserRole(Guid userId, string role)
    {
        UserId = userId;
        Role = role;
    }

    private PlatformUserRole()
    {
        Role = string.Empty;
    }

    public Guid UserId { get; private set; }

    public string Role { get; private set; }
}
