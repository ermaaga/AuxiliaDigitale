using Auxilia.Application.Abstractions.Settings;

namespace Auxilia.Application.Cases;

/// <summary>Settings of case expiry, used by the manual <c>cases.expiry</c> job (F11, D-15).</summary>
public static class CasesSettings
{
    public const string Module = "Cases";

    /// <summary>Legacy <c>AutoSubscriptionExpiry</c>.</summary>
    public static readonly SettingDefinition<bool> ExpiryEnabled = new("cases.expiry.enabled", Module, false);

    /// <summary>Legacy <c>SubscriptionExpiringDays</c>: days before the end date when a case is "expiring".</summary>
    public static readonly SettingDefinition<int> ExpiryExpiringDays = new(
        "cases.expiry.expiringDays", Module, 7, isValid: days => days is >= 1 and <= 365);

    public static IReadOnlyList<SettingDefinition> All { get; } = [ExpiryEnabled, ExpiryExpiringDays];
}
