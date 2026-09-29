using Auxilia.SharedKernel.Domain;

namespace Auxilia.Domain.Platform;

/// <summary>
/// A platform (System console) user (catalog <c>platform_users</c> + <c>platform_user_roles</c>). Login, mandatory
/// TOTP 2FA (D-22) and lockout are implemented with the platform identity (task P2); secrets are stored protected.
/// </summary>
public sealed class PlatformUser : AggregateRoot<Guid>
{
    public const int EmailMaxLength = 256;
    public const int DisplayNameMaxLength = 200;
    public const string SystemRole = "System";

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
        roles.Add(new PlatformUserRole(id, SystemRole));
    }

    private PlatformUser()
    {
        Email = DisplayName = string.Empty;
    }

    public string Email { get; private set; }

    public string DisplayName { get; private set; }

    /// <summary>Null until the user completes activation.</summary>
    public string? PasswordHash { get; private set; }

    /// <summary>TOTP secret protected with Data Protection; null until 2FA is enrolled.</summary>
    public string? TwoFactorSecret { get; private set; }

    public bool IsActive { get; private set; }

    public int AccessFailedCount { get; private set; }

    public DateTimeOffset? LockoutEnd { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public IReadOnlyCollection<PlatformUserRole> Roles => roles.AsReadOnly();

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
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
