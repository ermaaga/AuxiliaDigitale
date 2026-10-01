using Auxilia.Application.Abstractions.Settings;

namespace Auxilia.Application.Configuration;

/// <summary>How a coloured surface is painted (legacy theme and login background: gradient or solid colour).</summary>
public enum BrandingFill
{
    Gradient,
    Solid,
}

/// <summary>Login background (legacy <c>BackgroundConfigSection</c>): gradient, solid colour or an uploaded image.</summary>
public enum BackgroundKind
{
    Gradient,
    Solid,
    Image,
}

/// <summary>
/// Tenant branding (F23, legacy theme/background/<c>UseAppName</c>): settings of the Configuration module, edited by the
/// System in the console and served to everyone by the public branding endpoint. Colours are <c>#rgb</c>/<c>#rrggbb</c>;
/// the UI derives accessible tokens from them (P3-02). Logo and background image are files (<c>BrandingAsset</c>).
/// </summary>
public static class BrandingSettings
{
    public const string Module = "Configuration";

    /// <summary>Legacy default theme #667eea → #764ba2.</summary>
    public const string DefaultPrimary = "#667eea";

    public const string DefaultAccent = "#764ba2";

    public static readonly SettingDefinition<bool> UseAppName = new("branding.useAppName", Module, true);

    /// <summary>Legacy <c>AppName</c> (appsettings): now per tenant.</summary>
    public static readonly SettingDefinition<string> AppName = new(
        "branding.appName", Module, "Auxilia Digitale", isValid: value => !string.IsNullOrWhiteSpace(value) && value.Length <= 100);

    public static readonly SettingDefinition<BrandingFill> ThemeFill = new("branding.theme.fill", Module, BrandingFill.Gradient);

    public static readonly SettingDefinition<string> PrimaryColor = new("branding.theme.primaryColor", Module, DefaultPrimary, isValid: SettingRules.IsHexColor);

    public static readonly SettingDefinition<string> AccentColor = new("branding.theme.accentColor", Module, DefaultAccent, isValid: SettingRules.IsHexColor);

    public static readonly SettingDefinition<BackgroundKind> BackgroundType = new("branding.background.kind", Module, BackgroundKind.Gradient);

    public static readonly SettingDefinition<string> BackgroundStartColor = new("branding.background.startColor", Module, DefaultPrimary, isValid: SettingRules.IsHexColor);

    public static readonly SettingDefinition<string> BackgroundEndColor = new("branding.background.endColor", Module, DefaultAccent, isValid: SettingRules.IsHexColor);

    public static readonly SettingDefinition<string> BackgroundColor = new("branding.background.color", Module, DefaultPrimary, isValid: SettingRules.IsHexColor);

    public static IReadOnlyList<SettingDefinition> All { get; } =
    [
        UseAppName, AppName, ThemeFill, PrimaryColor, AccentColor, BackgroundType, BackgroundStartColor, BackgroundEndColor, BackgroundColor,
    ];
}
