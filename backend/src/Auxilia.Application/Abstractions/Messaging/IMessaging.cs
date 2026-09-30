using Auxilia.Application.Abstractions.Operations;

namespace Auxilia.Application.Abstractions.Messaging;

/// <summary>
/// Messages produced by an operation (transactional outbox, skill auxilia-messaging-rebus): the message is stored in
/// <c>ops.outbox_messages</c> in the operation's transaction and sent right after the commit. A message whose send
/// fails stays pending and is sent by the manual job <c>messaging.outbox</c> (no background polling, D-15).
/// </summary>
public interface IMessageOutbox
{
    /// <param name="scope">The running write operation: the message is committed with its changes.</param>
    /// <param name="message">A message of <c>Auxilia.Contracts.Messages</c>.</param>
    Task EnqueueAsync(IOperationScope scope, object message, CancellationToken cancellationToken);
}

/// <summary>Sends a message on the bus (routing by convention: <c>Messages.V&lt;n&gt;.&lt;Module&gt;</c> → <c>auxilia.&lt;module&gt;</c>).</summary>
public interface IMessageSender
{
    Task SendAsync(object message, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken);
}

/// <summary>A message stored in the outbox of the current tenant.</summary>
public sealed record OutboxEntry(Guid Id, string MessageType, string Body, IReadOnlyDictionary<string, string> Headers, DateTimeOffset CreatedAt);

/// <summary><c>ops.outbox_messages</c> of the current tenant.</summary>
public interface IOutboxStore
{
    /// <summary>Adds the entry in the transaction of the running operation.</summary>
    Task AddAsync(OutboxEntry entry, CancellationToken cancellationToken);

    /// <summary>Entries not sent yet: the given ones, or (<paramref name="ids"/> null) every entry created before <paramref name="createdBefore"/>.</summary>
    Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(IReadOnlyCollection<Guid>? ids, DateTimeOffset createdBefore, int maxCount, CancellationToken cancellationToken);

    /// <summary>Marks the entry as sent, outside any operation transaction.</summary>
    Task MarkDispatchedAsync(Guid id, DateTimeOffset dispatchedAt, CancellationToken cancellationToken);
}

/// <summary><c>ops.processed_messages</c> of the current tenant: the idempotency record of handlers.</summary>
public interface IProcessedMessageStore
{
    /// <summary>
    /// Records that <paramref name="handler"/> handled <paramref name="messageId"/>, in the running transaction;
    /// <c>false</c> when it was already recorded (duplicate delivery).
    /// </summary>
    Task<bool> TryMarkProcessedAsync(Guid messageId, string handler, CancellationToken cancellationToken);

    Task<bool> IsProcessedAsync(Guid messageId, string handler, CancellationToken cancellationToken);
}

/// <summary>Correlation id of the current request or message (propagated as <see cref="MessageHeaders.CorrelationId"/>).</summary>
public interface ICorrelationContext
{
    string CorrelationId { get; }
}
