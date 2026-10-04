using Auxilia.Application.Bus;
using Auxilia.Application.Marketing;
using Auxilia.Contracts.Messages.V1.Marketing;
using Auxilia.Infrastructure.Messaging;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using Rebus.Pipeline;

namespace Auxilia.Worker.Handlers.Marketing;

/// <summary>Sends a campaign as the user who asked for it (their roles choose the account); idempotent on the campaign status.</summary>
internal sealed class SendCampaignHandler(IIncomingMessageProcessor processor, IMessageContext context, MessageCurrentUser caller, ICampaignManager campaigns)
    : MessageHandler<SendCampaignCommand>(processor, context)
{
    protected override bool Transactional => false;

    protected override Task<Result> HandleAsync(SendCampaignCommand message, CancellationToken cancellationToken)
    {
        caller.SetRoles(message.Roles.Select(role => TenantRoles.TryParse(role, out var parsed) ? parsed : (TenantRole?)null).OfType<TenantRole>());
        return campaigns.ProcessAsync(message.CampaignId, cancellationToken);
    }
}
