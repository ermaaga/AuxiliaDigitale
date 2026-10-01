using System.Text.Json;

namespace Auxilia.Contracts.Messaging;

/// <summary>
/// A sending account of the tenant (N03, console): <see cref="Settings"/> are the provider's non-secret settings (for
/// <c>smtp</c>: host, port, security, username, fromAddress, fromName); the secret is never returned, only whether one
/// is stored.
/// </summary>
/// <param name="Channel"><c>Email</c>, <c>WhatsApp</c> or <c>Sms</c>.</param>
/// <param name="IsAvailable">Whether the web host has an adapter for the provider (WhatsApp is modelled only, D-20).</param>
public sealed record MessagingAccountResponse(
    Guid Id,
    string Channel,
    string Provider,
    string Name,
    JsonElement Settings,
    bool HasSecret,
    bool IsDefault,
    bool IsActive,
    bool IsAvailable);

/// <param name="Secret">Password or token, protected before it is stored.</param>
public sealed record CreateMessagingAccountRequest(string Channel, string Provider, string Name, JsonElement Settings, string? Secret);

/// <param name="Secret">A new secret, or null/empty to keep the stored one.</param>
public sealed record UpdateMessagingAccountRequest(string Name, JsonElement Settings, string? Secret);

public sealed record CreateMessagingAccountResponse(Guid Id);

public sealed record SetMessagingAccountActiveRequest(bool IsActive);

/// <param name="Purpose"><c>Transactional</c>, <c>Notification</c> or <c>Marketing</c>.</param>
/// <param name="Role">A tenant role, or null for any role.</param>
/// <param name="Priority">Lower wins among rules of the same specificity.</param>
public sealed record SenderRuleResponse(string Channel, string Purpose, string? Role, Guid AccountId, int Priority);

public sealed record SenderRuleRequest(string Purpose, string? Role, Guid AccountId, int Priority);

/// <summary><c>PUT /messaging/rules/{channel}</c>: the rules of the channel, replaced as a whole.</summary>
public sealed record SetSenderRulesRequest(IReadOnlyList<SenderRuleRequest> Rules);

public sealed record SendTestMessageRequest(string Recipient, string Language);

/// <summary>
/// The test message is sent right away and recorded in the outbound log whatever the outcome; <see cref="ErrorCode"/>
/// (<c>AUX-25021</c> credentials refused, <c>AUX-25022</c> server not reachable…) when it was not delivered.
/// </summary>
public sealed record SendTestMessageResponse(Guid MessageId, bool Sent, string? ErrorCode);

/// <summary>An entry of the outbound log (subject and body are not listed).</summary>
public sealed record OutboundMessageResponse(
    Guid Id,
    string Channel,
    string Purpose,
    Guid AccountId,
    string Recipient,
    string? TemplateCode,
    string Language,
    string Status,
    int Attempts,
    string? ErrorCode,
    DateTimeOffset QueuedAt,
    DateTimeOffset? CompletedAt);
