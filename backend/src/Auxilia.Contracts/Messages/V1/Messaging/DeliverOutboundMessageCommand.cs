namespace Auxilia.Contracts.Messages.V1.Messaging;

/// <summary>Delivers a queued outbound message (<c>messaging.outbound_messages</c>) through its account's channel.</summary>
public sealed record DeliverOutboundMessageCommand(Guid OutboundMessageId) : ITenantMessage;
