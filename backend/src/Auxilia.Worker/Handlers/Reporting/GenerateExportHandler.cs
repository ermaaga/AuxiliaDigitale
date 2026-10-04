using Auxilia.Application.Bus;
using Auxilia.Application.Reporting;
using Auxilia.Contracts.Messages.V1.Reporting;
using Auxilia.Infrastructure.Messaging;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using Rebus.Pipeline;

namespace Auxilia.Worker.Handlers.Reporting;

/// <summary>Writes a large export as the user who asked for it (their roles from the message); idempotent on the export status.</summary>
internal sealed class GenerateExportHandler(IIncomingMessageProcessor processor, IMessageContext context, MessageCurrentUser caller, IExportManager exports)
    : MessageHandler<GenerateExportCommand>(processor, context)
{
    protected override bool Transactional => false;

    protected override Task<Result> HandleAsync(GenerateExportCommand message, CancellationToken cancellationToken)
    {
        caller.SetRoles(message.Roles.Select(role => TenantRoles.TryParse(role, out var parsed) ? parsed : (TenantRole?)null).OfType<TenantRole>());
        return exports.GenerateAsync(message.ExportId, cancellationToken);
    }
}
