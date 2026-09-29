namespace Auxilia.Domain.Platform;

/// <summary>A platform-level default (catalog <c>platform_settings</c>, level 2 of ARCHITECTURE §7.1): key → JSON value, read through <c>ISettingsProvider</c>.</summary>
public sealed class PlatformSetting
{
    public const int KeyMaxLength = 150;

    public PlatformSetting(string key, string jsonValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(key.Length, KeyMaxLength);

        ArgumentException.ThrowIfNullOrWhiteSpace(jsonValue);

        Key = key;
        JsonValue = jsonValue;
    }

    private PlatformSetting()
    {
        Key = JsonValue = string.Empty;
    }

    public string Key { get; private set; }

    public string JsonValue { get; private set; }

    public void SetValue(string jsonValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonValue);
        JsonValue = jsonValue;
    }
}
