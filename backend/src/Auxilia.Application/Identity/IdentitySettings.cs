using Auxilia.Application.Abstractions.Settings;

namespace Auxilia.Application.Identity;

/// <summary>Session, password and lockout settings (legacy <c>SessionTimeout</c>, ARCHITECTURE §7.2).</summary>
public static class IdentitySettings
{
    public const string Module = "Identity";

    /// <summary>Minutes of inactivity before the session ends (legacy <c>SessionTimeout</c>, 120).</summary>
    public static readonly SettingDefinition<int> SessionIdleMinutes = new(
        "auth.session.idleMinutes", Module, 120, isValid: minutes => minutes is >= 5 and <= 1440);

    /// <summary>One active session per user (D-08, off by default).</summary>
    public static readonly SettingDefinition<bool> SingleSession = new("auth.singleSession", Module, false);

    /// <summary>Minimum password length (skill auxilia-security: 12). The full policy (history, expiry) comes with F35 (P2-08).</summary>
    public static readonly SettingDefinition<int> PasswordMinLength = new(
        "auth.password.minLength", Module, 12, isValid: length => length is >= 8 and <= 128);

    /// <summary>Failed sign-ins that lock the account.</summary>
    public static readonly SettingDefinition<int> LockoutMaxFailedAttempts = new(
        "auth.lockout.maxFailedAttempts", Module, 5, isValid: attempts => attempts is >= 3 and <= 50);

    /// <summary>First lockout duration; every further lockout doubles it (progressive, at most 24 h).</summary>
    public static readonly SettingDefinition<int> LockoutMinutes = new(
        "auth.lockout.minutes", Module, 5, isValid: minutes => minutes is >= 1 and <= 1440);

    public static IReadOnlyList<SettingDefinition> All { get; } =
        [SessionIdleMinutes, SingleSession, PasswordMinLength, LockoutMaxFailedAttempts, LockoutMinutes];
}
