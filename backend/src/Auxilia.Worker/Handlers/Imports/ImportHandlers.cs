using Auxilia.Application.Bus;
using Auxilia.Application.Imports;
using Auxilia.Contracts.Messages.V1.Imports;
using Auxilia.Infrastructure.Messaging;
using Auxilia.SharedKernel.Results;

using Rebus.Pipeline;

namespace Auxilia.Worker.Handlers.Imports;

/// <summary>Validates the rows of an uploaded import; idempotent on the import status.</summary>
internal sealed class ValidateImportHandler(IIncomingMessageProcessor processor, IMessageContext context, IImportManager imports)
    : MessageHandler<ValidateImportCommand>(processor, context)
{
    protected override bool Transactional => false;

    protected override Task<Result> HandleAsync(ValidateImportCommand message, CancellationToken cancellationToken) =>
        imports.ValidateAsync(message.ImportId, cancellationToken);
}

/// <summary>Imports the valid rows of a confirmed import; a redelivery continues with the rows still valid.</summary>
internal sealed class ProcessImportHandler(IIncomingMessageProcessor processor, IMessageContext context, IImportManager imports)
    : MessageHandler<ProcessImportCommand>(processor, context)
{
    protected override bool Transactional => false;

    protected override Task<Result> HandleAsync(ProcessImportCommand message, CancellationToken cancellationToken) =>
        imports.ProcessAsync(message.ImportId, cancellationToken);
}
