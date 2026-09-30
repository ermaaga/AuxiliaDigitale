using Auxilia.SharedKernel.Domain;

namespace Auxilia.Domain.Configuration;

/// <summary>
/// A tenant-level setting value (<c>configuration.settings</c>, level 3 of ARCHITECTURE §7.1): key → JSON value.
/// Secret values are stored already protected (Data Protection).
/// </summary>
public sealed class TenantSetting : AggregateRoot<Guid>, IAuditable
{
    public const int KeyMaxLength = 150;

    public TenantSetting(Guid id, string key, string jsonValue)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(key.Length, KeyMaxLength);
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonValue);

        Key = key;
        JsonValue = jsonValue;
    }

    private TenantSetting()
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
