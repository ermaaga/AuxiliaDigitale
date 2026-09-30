using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Bus;
using Auxilia.Contracts.Messages;
using Auxilia.Diagnostics;

using Microsoft.Extensions.Logging;

using Rebus.Bus;
using Rebus.Exceptions;
using Rebus.Handlers;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Retry.Simple;

namespace Auxilia.Infrastructure.Messaging;

/// <summary><see cref="IMessageSender"/> on Rebus; the headers (tenant, correlation, caller, trace, message id) come from the producer.</summary>
internal sealed class RebusMessageSender(IBus bus) : IMessageSender
{
    public Task SendAsync(object message, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(headers);

        return bus.Send(message, headers.ToDictionary(StringComparer.Ordinal));
    }
}

/// <summary>Without <c>ConnectionStrings:RabbitMq</c> nothing can be sent: outbox entries stay pending (logged, <c>bus.outbox</c> job).</summary>
internal sealed class UnavailableMessageSender : IMessageSender
{
    public Task SendAsync(object message, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The message bus is not configured (ConnectionStrings:RabbitMq).");
}

/// <summary>
/// Base of the Worker handlers (thin: call a Manager). Runs the handler through <see cref="IIncomingMessageProcessor"/>:
/// tenant, caller, correlation and trace from the headers, idempotency, one operation log; a failed
/// <see cref="SharedKernel.Results.Result"/> rejects the message (no retries).
/// </summary>
public abstract class MessageHandler<TMessage> : IHandleMessages<TMessage>
    where TMessage : class
{
    private readonly IIncomingMessageProcessor processor;
    private readonly IMessageContext context;

    protected MessageHandler(IIncomingMessageProcessor processor, IMessageContext context)
    {
        this.processor = processor;
        this.context = context;
    }

    /// <summary>
    /// <c>true</c>: the handler's changes and the idempotency record commit together. <c>false</c> for long work made of
    /// many operations (jobs): the record is written after success, so the work must be idempotent.
    /// </summary>
    protected virtual bool Transactional => true;

    public Task Handle(TMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var headers = context.Headers;
        var incoming = new IncomingMessage(
            Guid.TryParse(headers.GetValueOrDefault(Headers.MessageId), out var id) ? id : Guid.Empty,
            typeof(TMessage).Name,
            message is ITenantMessage,
            headers);
        var cancellationToken = context.IncomingStepContext.Load<CancellationToken>();

        return processor.HandleAsync(incoming, GetType().Name, Transactional, ct => HandleAsync(message, ct), cancellationToken);
    }

    protected abstract Task<SharedKernel.Results.Result> HandleAsync(TMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Second-level retries: after the immediate attempts the failed message is deferred with growing delays
/// (<see cref="MessageBusOptions.SecondLevelRetryDelays"/>), then moved to the error queue. A rejected message
/// (<see cref="MessageRejectedException"/>) goes to the error queue at once.
/// </summary>
internal sealed class SecondLevelRetryHandler<TMessage>(IBus bus, MessageBusOptions options, ILogger<SecondLevelRetryHandler<TMessage>> logger)
    : IHandleMessages<IFailed<TMessage>>
    where TMessage : class
{
    public const string AttemptHeader = "x-slr-attempt";

    public async Task Handle(IFailed<TMessage> message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var messageId = message.Headers.GetValueOrDefault(Headers.MessageId) ?? "?";
        var rejected = message.Exceptions.Any(exception => exception.Type == typeof(MessageRejectedException).FullName);
        var attempt = int.TryParse(message.Headers.GetValueOrDefault(AttemptHeader), out var previous) ? previous + 1 : 1;

        if (rejected || attempt > options.SecondLevelRetryDelays.Count)
        {
            Log.Bus.MessageDeadLettered(logger, typeof(TMessage).Name, messageId, rejected ? "rejected" : $"failed after {attempt - 1} second-level retries");
            await bus.Advanced.TransportMessage.Deadletter(message.ErrorDescription);
            return;
        }

        var delay = options.SecondLevelRetryDelays[attempt - 1];
        Log.Bus.MessageRetryScheduled(logger, typeof(TMessage).Name, messageId, attempt, delay.TotalSeconds);
        await bus.Advanced.TransportMessage.Defer(delay, new Dictionary<string, string>(StringComparer.Ordinal) { [AttemptHeader] = attempt.ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }
}
