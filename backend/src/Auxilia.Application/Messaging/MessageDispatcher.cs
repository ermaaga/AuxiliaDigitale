using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Channels;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Messaging.Public;
using Auxilia.Contracts.Messages.V1.Messaging;
using Auxilia.Diagnostics;
using Auxilia.Domain.Messaging;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Messaging;

/// <inheritdoc cref="IMessageDispatcher"/>
internal sealed class MessageDispatcher : IMessageDispatcher
{
    private readonly IOperationRunner operations;
    private readonly IMessagingDataFactory data;
    private readonly MessagingSnapshotCache snapshots;
    private readonly IEnumerable<IMessageChannel> channels;
    private readonly ITemplateRenderer templates;
    private readonly IMessageOutbox outbox;
    private readonly ITenantContext tenantContext;
    private readonly ICurrentUser currentUser;
    private readonly TimeProvider timeProvider;

    public MessageDispatcher(
        IOperationRunner operations,
        IMessagingDataFactory data,
        MessagingSnapshotCache snapshots,
        IEnumerable<IMessageChannel> channels,
        ITemplateRenderer templates,
        IMessageOutbox outbox,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        TimeProvider timeProvider)
    {
        this.operations = operations;
        this.data = data;
        this.snapshots = snapshots;
        this.channels = channels;
        this.templates = templates;
        this.outbox = outbox;
        this.tenantContext = tenantContext;
        this.currentUser = currentUser;
        this.timeProvider = timeProvider;
    }

    public Task<Result<Guid>> QueueAsync(OutboundMessageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Messaging.QueueMessage, new { request.Channel, request.Purpose, request.TemplateCode }, async scope =>
        {
            var roles = currentUser.Roles.Select(role => role.ToString()).ToArray();
            var snapshot = await snapshots.GetAsync(cancellationToken);
            if (SendingAccountResolution.Resolve(snapshot, request.Channel, request.Purpose, roles) is not { } account)
            {
                return Errors.Messaging.NoAccountForMessage(request.Channel.ToString(), request.Purpose.ToString());
            }

            var channel = channels.FirstOrDefault(item => string.Equals(item.Provider, account.Provider, StringComparison.Ordinal));
            if (channel is null)
            {
                return Errors.Messaging.ChannelNotAvailable(account.Provider);
            }

            if (!channel.IsValidRecipient(request.Recipient))
            {
                return Errors.Messaging.RecipientInvalid();
            }

            await using var store = await data.OpenAsync(cancellationToken);
            var content = await MessageContent.RenderAsync(
                store, templates, request.Channel, request.TemplateCode, request.Language, tenantContext.Tenant.DefaultLanguage, request.Model, cancellationToken);
            if (content.IsFailure)
            {
                return Result.Failure<Guid>(content.Error!);
            }

            var message = new OutboundMessage(
                Guid.CreateVersion7(), request.Channel, request.Purpose, account.Id, request.Recipient.Trim(), request.TemplateCode,
                content.Value.Language, content.Value.Subject, content.Value.Body, timeProvider.GetUtcNow(),
                request.RelatedEntityType, request.RelatedEntityId);
            store.Add(message);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity("OutboundMessage", message.Id);

            await outbox.EnqueueAsync(scope, new DeliverOutboundMessageCommand(message.Id), cancellationToken);
            return Result.Success(message.Id);
        }, cancellationToken);
    }
}
