using Auxilia.SharedKernel.Domain;

namespace Auxilia.Domain.Configuration;

/// <summary>
/// A user-level setting value (<c>configuration.user_settings</c>, level 4 of ARCHITECTURE §7.1) for the settings whose
/// definition allows the user scope. Never secret.
/// </summary>
public sealed class UserSetting : AggregateRoot<Guid>, IAuditable
{
    public UserSetting(Guid id, Guid userId, string key, string jsonValue)
        : base(id)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("The user id is required.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(key.Length, TenantSetting.KeyMaxLength);
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonValue);

        UserId = userId;
        Key = key;
        JsonValue = jsonValue;
    }

    private UserSetting()
    {
        Key = JsonValue = string.Empty;
    }

    public Guid UserId { get; private set; }

    public string Key { get; private set; }

    public string JsonValue { get; private set; }

    public void SetValue(string jsonValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonValue);
        JsonValue = jsonValue;
    }
}
