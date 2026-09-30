using Auxilia.Application.Bus;
using Auxilia.Application.Messaging;
using Auxilia.Contracts.Messages.V1.Messaging;
using Auxilia.Infrastructure.Messaging;
using Auxilia.SharedKernel.Results;

using Rebus.Pipeline;

namespace Auxilia.Worker.Handlers.Messaging;

/// <summary>
/// Delivers a queued outbound message. Not one transaction: sending is an external side effect and each state change
/// (attempt, Sent, Failed) is saved on its own; duplicates are recognised by the message status.
/// </summary>
internal sealed class DeliverOutboundMessageHandler(IIncomingMessageProcessor processor, IMessageContext context, IOutboundMessageManager messages)
    : MessageHandler<DeliverOutboundMessageCommand>(processor, context)
{
    protected override bool Transactional => false;

    protected override Task<Result> HandleAsync(DeliverOutboundMessageCommand message, CancellationToken cancellationToken) =>
        messages.DeliverAsync(message.OutboundMessageId, cancellationToken);
}
