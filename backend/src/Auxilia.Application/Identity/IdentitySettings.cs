using Auxilia.Application.Abstractions.Settings;

namespace Auxilia.Application.Identity;

/// <summary>Session settings (legacy <c>SessionTimeout</c>, ARCHITECTURE §7.2).</summary>
public static class IdentitySettings
{
    public const string Module = "Identity";

    /// <summary>Minutes of inactivity before the session ends (legacy <c>SessionTimeout</c>, 120).</summary>
    public static readonly SettingDefinition<int> SessionIdleMinutes = new(
        "auth.session.idleMinutes", Module, 120, isValid: minutes => minutes is >= 5 and <= 1440);

    /// <summary>One active session per user (D-08, off by default).</summary>
    public static readonly SettingDefinition<bool> SingleSession = new("auth.singleSession", Module, false);

    public static IReadOnlyList<SettingDefinition> All { get; } = [SessionIdleMinutes, SingleSession];
}
