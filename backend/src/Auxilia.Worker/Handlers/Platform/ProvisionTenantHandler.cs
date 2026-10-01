using Auxilia.Application.Bus;
using Auxilia.Application.Platform;
using Auxilia.Contracts.Messages.V1.Platform;
using Auxilia.Infrastructure.Messaging;
using Auxilia.SharedKernel.Results;

using Rebus.Pipeline;

namespace Auxilia.Worker.Handlers.Platform;

/// <summary>
/// Provisioning requested from the console (N02): not a tenant message (the tenant has no database yet). The work is
/// resumable and idempotent, so a retried or redelivered message is safe; a failure leaves the tenant in
/// MigrationFailed with its run in <c>catalog.migration_runs</c>.
/// </summary>
internal sealed class ProvisionTenantHandler(IIncomingMessageProcessor processor, IMessageContext context, ITenantProvisioningWorkflow workflow)
    : MessageHandler<ProvisionTenantCommand>(processor, context)
{
    protected override bool Transactional => false;

    protected override Task<Result> HandleAsync(ProvisionTenantCommand message, CancellationToken cancellationToken) =>
        workflow.RunAsync(message, cancellationToken);
}
