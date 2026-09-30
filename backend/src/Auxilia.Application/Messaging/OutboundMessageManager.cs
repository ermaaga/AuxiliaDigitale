using Auxilia.Application.Abstractions.Channels;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Messaging;

/// <summary>Delivery of queued outbound messages (Worker, queue <c>auxilia.messaging</c>).</summary>
public interface IOutboundMessageManager
{
    /// <summary>
    /// Sends a Queued message and records the outcome: Sent; Failed for permanent failures (failed result, not retried);
    /// a transient failure records the attempt and throws so the bus retries. A message already Sent or Failed is left
    /// as it is (duplicate delivery). At-least-once: a crash between the send and the record may send it twice.
    /// </summary>
    Task<Result> DeliverAsync(Guid outboundMessageId, CancellationToken cancellationToken);
}

internal sealed class OutboundMessageManager : IOutboundMessageManager
{
    private readonly IOperationRunner operations;
    private readonly IMessagingDataFactory data;
    private readonly IEnumerable<IMessageChannel> channels;
    private readonly IAccountSecretProtector secrets;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<OutboundMessageManager> logger;

    public OutboundMessageManager(
        IOperationRunner operations,
        IMessagingDataFactory data,
        IEnumerable<IMessageChannel> channels,
        IAccountSecretProtector secrets,
        TimeProvider timeProvider,
        ILogger<OutboundMessageManager> logger)
    {
        this.operations = operations;
        this.data = data;
        this.channels = channels;
        this.secrets = secrets;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public Task<Result> DeliverAsync(Guid outboundMessageId, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Messaging.DeliverMessage, new { OutboundMessageId = outboundMessageId }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindOutboundAsync(outboundMessageId, cancellationToken) is not { } message)
            {
                return Errors.Messaging.OutboundMessageNotFound();
            }

            if (message.IsFinal)
            {
                return Result.Success();
            }

            var account = await store.FindAccountAsync(message.AccountId, cancellationToken);
            var channel = account is null ? null : channels.FirstOrDefault(item => string.Equals(item.Provider, account.Provider, StringComparison.Ordinal));
            Result outcome;
            if (account is null || !account.IsActive)
            {
                outcome = Errors.Messaging.AccountNotFound();
            }
            else if (channel is null)
            {
                outcome = Errors.Messaging.ChannelNotAvailable(account.Provider);
            }
            else
            {
                try
                {
                    outcome = await DeliverySteps.SendAsync(channel, account, message, secrets, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    message.RecordFailedAttempt($"AUX-{EventCodes.Messaging.DeliveryAttemptFailed}");
                    await store.SaveChangesAsync(CancellationToken.None);
                    Log.Messaging.DeliveryAttemptFailed(logger, exception, message.Attempts, message.Id);
                    throw;
                }
            }

            if (outcome.IsSuccess)
            {
                message.MarkSent(timeProvider.GetUtcNow());
            }
            else
            {
                message.MarkFailed(outcome.Error!.DisplayCode, timeProvider.GetUtcNow());
                Log.Messaging.MessageFailed(logger, message.Id, outcome.Error.DisplayCode);
            }

            await store.SaveChangesAsync(CancellationToken.None);
            return outcome;
        }, cancellationToken);
}
