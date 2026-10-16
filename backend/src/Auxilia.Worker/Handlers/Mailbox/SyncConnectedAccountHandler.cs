using Auxilia.Application.Bus;
using Auxilia.Application.Mailbox;
using Auxilia.Contracts.Messages.V1.Mailbox;
using Auxilia.Infrastructure.Messaging;
using Auxilia.SharedKernel.Results;

using Rebus.Pipeline;

namespace Auxilia.Worker.Handlers.Mailbox;

/// <summary>
/// One part of a sync run (N05, #106). Not transactional: every chunk commits on its own with its cursor, so a
/// redelivery resumes from the last saved chunk; a provider that cannot be reached throws and the bus retries.
/// </summary>
internal sealed class SyncConnectedAccountHandler(IIncomingMessageProcessor processor, IMessageContext context, IAccountSyncService sync)
    : MessageHandler<SyncConnectedAccountCommand>(processor, context)
{
    protected override bool Transactional => false;

    protected override Task<Result> HandleAsync(SyncConnectedAccountCommand message, CancellationToken cancellationToken) =>
        sync.SyncAsync(message.RunId, message.Part, cancellationToken);
}
