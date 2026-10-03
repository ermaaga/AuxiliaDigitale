using Auxilia.Application.Abstractions.Settings;

namespace Auxilia.Application.Directory;

/// <summary>Settings of external registration (legacy <c>Registration*</c> keys, ARCHITECTURE §7.2; API only, D-14).</summary>
public static class DirectorySettings
{
    public const string Module = "Directory";

    /// <summary>Legacy <c>RegistrationEnabled</c>; off by default because there is no public page (D-14).</summary>
    public static readonly SettingDefinition<bool> RegistrationEnabled = new("registration.enabled", Module, false);

    /// <summary>Legacy <c>SendRegistrationConfirmationEmail</c>.</summary>
    public static readonly SettingDefinition<bool> RegistrationSendConfirmationEmail = new("registration.sendConfirmationEmail", Module, false);

    /// <summary>Off by default: there is no approval page yet (D-27).</summary>
    public static readonly SettingDefinition<bool> RegistrationNotifyAdmins = new("registration.notifyAdmins", Module, false);

    /// <summary>Legacy <c>RegistrationLanguage</c>: language of the confirmation e-mails.</summary>
    public static readonly SettingDefinition<string> RegistrationDefaultLanguage = new(
        "registration.defaultLanguage", Module, "it", isValid: SettingRules.IsLanguageCode);

    /// <summary>Q59: the applicant must be at least this old (legacy: a fixed birth date range meaning 16).</summary>
    public static readonly SettingDefinition<int> RegistrationMinimumAge = new(
        "registration.minimumAge", Module, 16, isValid: years => years is >= 0 and <= 120);

    public static IReadOnlyList<SettingDefinition> All { get; } =
        [RegistrationEnabled, RegistrationSendConfirmationEmail, RegistrationNotifyAdmins, RegistrationDefaultLanguage, RegistrationMinimumAge];
}
