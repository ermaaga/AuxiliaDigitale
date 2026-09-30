namespace Auxilia.Domain.Messaging;

/// <summary>Outbound channel (N03). Only <see cref="Email"/> has an adapter today; WhatsApp is modelled for later (D-20).</summary>
public enum MessageChannel
{
    Email,
    WhatsApp,
    Sms,
}

/// <summary>Why a message is sent: selects the sending account together with the sender's role (D-16).</summary>
public enum MessagePurpose
{
    Transactional,
    Notification,
    Marketing,
}

public enum OutboundMessageStatus
{
    Queued,
    Sent,
    Failed,
}
