using Auxilia.Application.Abstractions.Jobs;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Bus;

/// <inheritdoc cref="IMessageOutbox"/>
internal sealed class MessageOutbox : IMessageOutbox
{
    private readonly IOutboxStore store;
    private readonly OutboxDispatcher dispatcher;
    private readonly OutgoingMessageHeaders headers;
    private readonly TimeProvider timeProvider;

    public MessageOutbox(IOutboxStore store, OutboxDispatcher dispatcher, OutgoingMessageHeaders headers, TimeProvider timeProvider)
    {
        this.store = store;
        this.dispatcher = dispatcher;
        this.headers = headers;
        this.timeProvider = timeProvider;
    }

    public async Task EnqueueAsync(IOperationScope scope, object message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(message);

        var id = Guid.CreateVersion7();
        var entry = new OutboxEntry(id, MessageTypes.NameOf(message), MessageTypes.Serialize(message), headers.Create(id), timeProvider.GetUtcNow());
        await store.AddAsync(entry, cancellationToken);
        scope.OnCommitted(ct => dispatcher.DispatchAsync([id], ct));
    }
}

/// <summary>Sends pending outbox entries and marks them; a failed send leaves the entry pending (logged).</summary>
internal sealed class OutboxDispatcher
{
    public const int BatchSize = 500;

    private readonly IOutboxStore store;
    private readonly IMessageSender sender;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<OutboxDispatcher> logger;

    public OutboxDispatcher(IOutboxStore store, IMessageSender sender, TimeProvider timeProvider, ILogger<OutboxDispatcher> logger)
    {
        this.store = store;
        this.sender = sender;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <returns>Sent and failed entries.</returns>
    public async Task<(int Sent, int Failed)> DispatchAsync(IReadOnlyCollection<Guid>? ids, CancellationToken cancellationToken, TimeSpan? minimumAge = null)
    {
        var pending = await store.GetPendingAsync(ids, timeProvider.GetUtcNow() - (minimumAge ?? TimeSpan.Zero), BatchSize, cancellationToken);
        var (sent, failed) = (0, 0);
        foreach (var entry in pending)
        {
            try
            {
                await sender.SendAsync(MessageTypes.Deserialize(entry.MessageType, entry.Body), entry.Headers, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Log.Bus.OutboxDispatchFailed(logger, exception, entry.Id, entry.MessageType);
                failed++;
                continue;
            }

            // A crash here sends the message again later: handlers are idempotent (ops.processed_messages).
            await store.MarkDispatchedAsync(entry.Id, timeProvider.GetUtcNow(), CancellationToken.None);
            sent++;
        }

        return (sent, failed);
    }
}

/// <summary>
/// Sends the outbox entries of the tenant left pending (send failed after commit, or process stopped): run by the
/// System when the bus was down (D-15, no background polling). Entries younger than a minute are left to the
/// operation that is sending them.
/// </summary>
internal sealed class OutboxDispatchJob : IRecurringJob
{
    public const string JobCode = "bus.outbox";

    private readonly OutboxDispatcher dispatcher;

    public OutboxDispatchJob(OutboxDispatcher dispatcher) => this.dispatcher = dispatcher;

    public string Code => JobCode;

    public string Description => "Sends the messages left in the outbox (e.g. after a RabbitMQ outage)";

    public string SuggestedFrequency => "after a message bus outage";

    public async Task<Result<string>> RunAsync(CancellationToken cancellationToken)
    {
        var (sent, failed) = await dispatcher.DispatchAsync(null, cancellationToken, TimeSpan.FromMinutes(1));
        return failed == 0
            ? Result.Success($"sent {sent}")
            : Errors.Jobs.JobRunFailed(JobCode);
    }
}
