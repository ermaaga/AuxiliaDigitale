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

    /// <summary>Minimum password length (skill auxilia-security: 12; the legacy default was 8, F35).</summary>
    public static readonly SettingDefinition<int> PasswordMinLength = new(
        "auth.password.minLength", Module, 12, isValid: length => length is >= 8 and <= 128);

    /// <summary>F35: at least one upper-case letter.</summary>
    public static readonly SettingDefinition<bool> PasswordRequireUppercase = new("auth.password.requireUppercase", Module, true);

    /// <summary>F35: at least one lower-case letter.</summary>
    public static readonly SettingDefinition<bool> PasswordRequireLowercase = new("auth.password.requireLowercase", Module, true);

    /// <summary>F35: at least one digit.</summary>
    public static readonly SettingDefinition<bool> PasswordRequireDigit = new("auth.password.requireDigit", Module, true);

    /// <summary>F35: at least one character that is neither a letter nor a digit.</summary>
    public static readonly SettingDefinition<bool> PasswordRequireSpecial = new("auth.password.requireSpecial", Module, true);

    /// <summary>F35: a new password may not match any of the last N (0 = no history rule).</summary>
    public static readonly SettingDefinition<int> PasswordHistoryCount = new(
        "auth.password.historyCount", Module, 3, isValid: count => count is >= 0 and <= Domain.Identity.User.MaxPasswordHistory);

    /// <summary>F35: passwords expire (off by default); an expired password must be changed before signing in.</summary>
    public static readonly SettingDefinition<bool> PasswordExpiryEnabled = new("auth.password.expiryEnabled", Module, false);

    /// <summary>F35: age of an expired password, in months.</summary>
    public static readonly SettingDefinition<int> PasswordExpiryMonths = new(
        "auth.password.expiryMonths", Module, 6, isValid: months => months is >= 1 and <= 36);

    /// <summary>F35: sign-in with a one-time code sent by e-mail (method <c>email-otp</c>), off by default.</summary>
    public static readonly SettingDefinition<bool> OtpLoginEnabled = new("auth.otp.enabled", Module, false);

    /// <summary>F35: validity of the e-mailed sign-in code.</summary>
    public static readonly SettingDefinition<int> OtpCodeMinutes = new(
        "auth.otp.codeMinutes", Module, 10, isValid: minutes => minutes is >= 1 and <= 60);

    /// <summary>Failed sign-ins that lock the account.</summary>
    public static readonly SettingDefinition<int> LockoutMaxFailedAttempts = new(
        "auth.lockout.maxFailedAttempts", Module, 5, isValid: attempts => attempts is >= 3 and <= 50);

    /// <summary>First lockout duration; every further lockout doubles it (progressive, at most 24 h).</summary>
    public static readonly SettingDefinition<int> LockoutMinutes = new(
        "auth.lockout.minutes", Module, 5, isValid: minutes => minutes is >= 1 and <= 1440);

    /// <summary>Lifetime of access tokens (skill auxilia-security: 10–15 min).</summary>
    public static readonly SettingDefinition<int> AccessTokenMinutes = new(
        "auth.accessToken.minutes", Module, 10, isValid: minutes => minutes is >= 5 and <= 60);

    /// <summary>Absolute lifetime of a sign-in session; the idle timeout is <see cref="SessionIdleMinutes"/>.</summary>
    public static readonly SettingDefinition<int> SessionAbsoluteDays = new(
        "auth.session.absoluteDays", Module, 14, isValid: days => days is >= 1 and <= 90);

    /// <summary>Validity of account activation links (D-06).</summary>
    public static readonly SettingDefinition<int> ActivationLinkHours = new(
        "auth.activation.linkHours", Module, 72, isValid: hours => hours is >= 1 and <= 720);

    /// <summary>Validity of password reset links (short, skill auxilia-security).</summary>
    public static readonly SettingDefinition<int> PasswordResetLinkMinutes = new(
        "auth.passwordReset.linkMinutes", Module, 60, isValid: minutes => minutes is >= 5 and <= 1440);

    /// <summary>Public URL of the web app; links in e-mails are <c>{appBaseUrl}/{tenant}/…</c>.</summary>
    public static readonly SettingDefinition<string> AppBaseUrl = new(
        "auth.appBaseUrl", Module, "http://localhost:3000",
        isValid: url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !url.EndsWith('/'));

    public static IReadOnlyList<SettingDefinition> All { get; } =
    [
        SessionIdleMinutes, SingleSession, PasswordMinLength, LockoutMaxFailedAttempts, LockoutMinutes,
        AccessTokenMinutes, SessionAbsoluteDays, ActivationLinkHours, PasswordResetLinkMinutes, AppBaseUrl,
        PasswordRequireUppercase, PasswordRequireLowercase, PasswordRequireDigit, PasswordRequireSpecial,
        PasswordHistoryCount, PasswordExpiryEnabled, PasswordExpiryMonths, OtpLoginEnabled, OtpCodeMinutes,
    ];
}
