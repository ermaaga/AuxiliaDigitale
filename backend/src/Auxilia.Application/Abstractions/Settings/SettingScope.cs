namespace Auxilia.Application.Abstractions.Settings;

/// <summary>
/// Levels where a setting can be stored (ARCHITECTURE §7.1). Level 0 (infrastructure, <c>appsettings</c>) and level 1
/// (the code default of the definition) are not stored here. The effective value is user → tenant → platform →
/// default, skipping the levels the definition does not allow.
/// </summary>
[Flags]
public enum SettingScope
{
    None = 0,

    /// <summary>Level 2: <c>catalog.platform_settings</c>, default for every tenant (System).</summary>
    Platform = 1,

    /// <summary>Level 3: <c>configuration.settings</c> in the tenant database (System, from the console).</summary>
    Tenant = 2,

    /// <summary>Level 4: <c>configuration.user_settings</c> in the tenant database (the user).</summary>
    User = 4,

    PlatformAndTenant = Platform | Tenant,
}

/// <summary>Where the effective value of a setting comes from.</summary>
public enum SettingSource
{
    Default,
    Platform,
    Tenant,
    User,
}
