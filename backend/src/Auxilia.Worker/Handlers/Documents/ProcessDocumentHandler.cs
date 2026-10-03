using Auxilia.Application.Bus;
using Auxilia.Application.Documents;
using Auxilia.Contracts.Messages.V1.Documents;
using Auxilia.Infrastructure.Messaging;
using Auxilia.SharedKernel.Results;

using Rebus.Pipeline;

namespace Auxilia.Worker.Handlers.Documents;

/// <summary>Checks a document after its upload; idempotent on the document status (a redelivery does nothing).</summary>
internal sealed class ProcessDocumentHandler(IIncomingMessageProcessor processor, IMessageContext context, IDocumentManager documents)
    : MessageHandler<ProcessDocumentCommand>(processor, context)
{
    protected override bool Transactional => false;

    protected override Task<Result> HandleAsync(ProcessDocumentCommand message, CancellationToken cancellationToken) =>
        documents.ProcessAsync(message.DocumentId, cancellationToken);
}
