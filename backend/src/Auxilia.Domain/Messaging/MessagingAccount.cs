using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Messaging;

/// <summary>
/// A sending account of a channel (<c>configuration.messaging_accounts</c>, N03): provider (<c>smtp</c>,
/// <c>http-gateway</c>…), non-secret settings as JSON and the secret already protected with Data Protection. Exactly one
/// default account per channel; the default cannot be deactivated.
/// </summary>
public sealed class MessagingAccount : AggregateRoot<Guid>, IAuditable
{
    public const int ProviderMaxLength = 50;
    public const int NameMaxLength = 100;

    private MessagingAccount(Guid id, MessageChannel channel, string provider, string name, string settingsJson, string? secretProtected)
        : base(id)
    {
        Channel = channel;
        Provider = provider;
        Name = name;
        SettingsJson = settingsJson;
        SecretProtected = secretProtected;
        IsActive = true;
    }

    private MessagingAccount()
    {
        Provider = Name = SettingsJson = string.Empty;
    }

    public MessageChannel Channel { get; private set; }

    public string Provider { get; private set; }

    public string Name { get; private set; }

    public string SettingsJson { get; private set; }

    public string? SecretProtected { get; private set; }

    public bool IsDefault { get; private set; }

    public bool IsActive { get; private set; }

    public static Result<MessagingAccount> Create(Guid id, MessageChannel channel, string provider, string name, string settingsJson, string? secretProtected)
    {
        var invalid = Validate(provider, name, settingsJson);
        return invalid is null
            ? new MessagingAccount(id, channel, provider, name.Trim(), settingsJson, secretProtected)
            : invalid;
    }

    /// <param name="secretProtected">New protected secret; <c>null</c> keeps the current one.</param>
    public Result Update(string name, string settingsJson, string? secretProtected)
    {
        if (Validate(Provider, name, settingsJson) is { } invalid)
        {
            return invalid;
        }

        Name = name.Trim();
        SettingsJson = settingsJson;
        SecretProtected = secretProtected ?? SecretProtected;
        return Result.Success();
    }

    /// <summary>Becomes (or stops being) the default of its channel; the caller clears the previous default.</summary>
    public Result SetDefault(bool isDefault)
    {
        if (isDefault && !IsActive)
        {
            return Errors.Messaging.DefaultAccountMustBeActive();
        }

        IsDefault = isDefault;
        return Result.Success();
    }

    public Result SetActive(bool isActive)
    {
        if (!isActive && IsDefault)
        {
            return Errors.Messaging.DefaultAccountMustBeActive();
        }

        IsActive = isActive;
        return Result.Success();
    }

    private static Error? Validate(string provider, string name, string settingsJson)
    {
        if (string.IsNullOrWhiteSpace(provider) || provider.Length > ProviderMaxLength)
        {
            return Errors.Messaging.AccountSettingsInvalid("provider");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMaxLength)
        {
            return Errors.Messaging.AccountSettingsInvalid("name");
        }

        return string.IsNullOrWhiteSpace(settingsJson) ? Errors.Messaging.AccountSettingsInvalid("settings") : null;
    }
}

/// <summary>
/// Which account sends messages of a channel and purpose caused by a role (<c>configuration.sender_rules</c>, D-16).
/// <see cref="Role"/> null = any role; lower <see cref="Priority"/> wins among rules of the same specificity.
/// </summary>
public sealed class SenderRule : Entity<Guid>, IAuditable
{
    public const int RoleMaxLength = 30;

    public SenderRule(Guid id, MessageChannel channel, MessagePurpose purpose, string? role, Guid accountId, int priority)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(role?.Length ?? 0, RoleMaxLength);

        Channel = channel;
        Purpose = purpose;
        Role = role;
        AccountId = accountId;
        Priority = priority;
    }

    private SenderRule()
    {
    }

    public MessageChannel Channel { get; private set; }

    public MessagePurpose Purpose { get; private set; }

    public string? Role { get; private set; }

    public Guid AccountId { get; private set; }

    public int Priority { get; private set; }
}
