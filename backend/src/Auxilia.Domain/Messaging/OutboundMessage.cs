using Auxilia.SharedKernel.Domain;

namespace Auxilia.Domain.Messaging;

/// <summary>
/// Log of an outbound message (<c>messaging.outbound_messages</c>, ARCHITECTURE §8.2): answers "was the e-mail sent?".
/// Rendered subject and body are kept so the Worker sends exactly what was queued. Queued → Sent | Failed.
/// </summary>
public sealed class OutboundMessage : AggregateRoot<Guid>, IAuditable
{
    public const int RecipientMaxLength = 320;
    public const int ErrorCodeMaxLength = 20;
    public const int EntityTypeMaxLength = 100;

    public OutboundMessage(
        Guid id,
        MessageChannel channel,
        MessagePurpose purpose,
        Guid accountId,
        string recipient,
        string? templateCode,
        string language,
        string subject,
        string body,
        DateTimeOffset queuedAt,
        string? relatedEntityType = null,
        Guid? relatedEntityId = null)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(recipient.Length, RecipientMaxLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(templateCode?.Length ?? 0, MessageTemplate.CodeMaxLength);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(relatedEntityType?.Length ?? 0, EntityTypeMaxLength);

        Channel = channel;
        Purpose = purpose;
        AccountId = accountId;
        Recipient = recipient;
        TemplateCode = templateCode;
        Language = language;
        Subject = subject;
        Body = body;
        QueuedAt = queuedAt;
        RelatedEntityType = relatedEntityType;
        RelatedEntityId = relatedEntityId;
        Status = OutboundMessageStatus.Queued;
    }

    private OutboundMessage()
    {
        Recipient = Language = Subject = Body = string.Empty;
    }

    public MessageChannel Channel { get; private set; }

    public MessagePurpose Purpose { get; private set; }

    public Guid AccountId { get; private set; }

    public string Recipient { get; private set; }

    public string? TemplateCode { get; private set; }

    public string Language { get; private set; }

    public string Subject { get; private set; }

    public string Body { get; private set; }

    public string? RelatedEntityType { get; private set; }

    public Guid? RelatedEntityId { get; private set; }

    public OutboundMessageStatus Status { get; private set; }

    public int Attempts { get; private set; }

    /// <summary>Code of the last failure (<c>AUX-25xxx</c>), also after a failed attempt that will be retried.</summary>
    public string? ErrorCode { get; private set; }

    public DateTimeOffset QueuedAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public DateTimeOffset? FailedAt { get; private set; }

    public bool IsFinal => Status != OutboundMessageStatus.Queued;

    public void MarkSent(DateTimeOffset at)
    {
        EnsureQueued();
        Attempts++;
        Status = OutboundMessageStatus.Sent;
        SentAt = at;
        ErrorCode = null;
    }

    /// <summary>A failed attempt that the bus will retry.</summary>
    public void RecordFailedAttempt(string errorCode)
    {
        EnsureQueued();
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        Attempts++;
        ErrorCode = errorCode;
    }

    /// <summary>Permanent failure: no more attempts.</summary>
    public void MarkFailed(string errorCode, DateTimeOffset at)
    {
        EnsureQueued();
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        Attempts++;
        Status = OutboundMessageStatus.Failed;
        ErrorCode = errorCode;
        FailedAt = at;
    }

    private void EnsureQueued()
    {
        if (Status != OutboundMessageStatus.Queued)
        {
            throw new InvalidOperationException($"Outbound message {Id} is already {Status}.");
        }
    }
}
