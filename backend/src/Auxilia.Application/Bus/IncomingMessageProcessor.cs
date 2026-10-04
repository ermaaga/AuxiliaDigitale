using System.Diagnostics;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Execution;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Bus;

/// <summary>An incoming message as the processor sees it (transport independent).</summary>
public sealed record IncomingMessage(Guid MessageId, string MessageType, bool IsTenantMessage, IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// A message that must not be retried (validation, not found, missing or inactive tenant): the bus moves it to the
/// error queue at once. Carries the <see cref="Error"/> with its <c>AUX-</c> code.
/// </summary>
public sealed class MessageRejectedException : Exception
{
    public MessageRejectedException(Error error)
        : base($"{error?.DisplayCode}: {error?.Description}")
    {
        ArgumentNullException.ThrowIfNull(error);
        Error = error;
    }

    public MessageRejectedException()
        : this(Errors.Host.Unexpected())
    {
    }

    public MessageRejectedException(string message)
        : base(message)
    {
        Error = Errors.Host.Unexpected();
    }

    public MessageRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
        Error = Errors.Host.Unexpected();
    }

    public Error Error { get; }
}

/// <summary>The caller of a message handler, from the message headers (audit shows who caused the message).</summary>
public sealed class MessageCurrentUser : ICurrentUser
{
    public ActorType ActorType { get; private set; } = ActorType.System;

    public Guid? UserId { get; private set; }

    /// <summary>Empty unless a handler acts as the user with the roles the message carries (e.g. a queued export).</summary>
    public IReadOnlyCollection<TenantRole> Roles { get; private set; } = [];

    public void Set(ActorType actorType, Guid? userId)
    {
        ActorType = actorType;
        UserId = userId;
    }

    /// <summary>The roles of the user who caused the message, as the message carries them (trusted: it comes from the outbox).</summary>
    public void SetRoles(IEnumerable<TenantRole> roles) => Roles = roles.ToArray();
}

/// <summary>
/// Runs a handler for an incoming message (skill auxilia-messaging-rebus): correlation and caller from the headers,
/// tenant from <c>x-tenant-slug</c> (missing or inactive → rejected), trace joined to the sender's
/// <c>traceparent</c>, one <see cref="Operations.Bus.HandleMessage"/> operation. For tenant messages the idempotency
/// record (<c>ops.processed_messages</c>) is written in the same transaction as the handler's changes; handlers that are
/// not transactional (long jobs made of many operations) are marked after success and must be idempotent themselves.
/// A handler failure (<see cref="Result"/>) is permanent and rejects the message; exceptions are retried by the bus.
/// </summary>
public interface IIncomingMessageProcessor
{
    Task HandleAsync(IncomingMessage message, string handler, bool transactional, Func<CancellationToken, Task<Result>> work, CancellationToken cancellationToken);
}

internal sealed class IncomingMessageProcessor : IIncomingMessageProcessor
{
    private readonly ITenantDirectory tenants;
    private readonly ITenantContextSetter tenantContext;
    private readonly ICurrentUser currentUser;
    private readonly CorrelationContext correlation;
    private readonly IOperationRunner operations;
    private readonly IOperationTransactionFactory transactions;
    private readonly IProcessedMessageStore processed;
    private readonly ILogger<IncomingMessageProcessor> logger;

    public IncomingMessageProcessor(
        ITenantDirectory tenants,
        ITenantContextSetter tenantContext,
        ICurrentUser currentUser,
        CorrelationContext correlation,
        IOperationRunner operations,
        IOperationTransactionFactory transactions,
        IProcessedMessageStore processed,
        ILogger<IncomingMessageProcessor> logger)
    {
        this.tenants = tenants;
        this.tenantContext = tenantContext;
        this.currentUser = currentUser;
        this.correlation = correlation;
        this.operations = operations;
        this.transactions = transactions;
        this.processed = processed;
        this.logger = logger;
    }

    public async Task HandleAsync(
        IncomingMessage message, string handler, bool transactional, Func<CancellationToken, Task<Result>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(handler);
        ArgumentNullException.ThrowIfNull(work);

        correlation.Set(message.Headers.GetValueOrDefault(MessageHeaders.CorrelationId) is { Length: > 0 } correlationId ? correlationId : message.MessageId.ToString());
        SetCaller(message.Headers);

        if (message.IsTenantMessage)
        {
            await SetTenantAsync(message, cancellationToken);
        }

        using var activity = StartActivity(message);
        var result = await operations.RunAsync(
            Operations.Bus.HandleMessage,
            new { MessageId = message.MessageId, MessageType = message.MessageType, Handler = handler, CorrelationId = correlation.CorrelationId },
            _ => message.IsTenantMessage ? RunIdempotentAsync(message, handler, transactional, work, cancellationToken) : work(cancellationToken),
            cancellationToken);

        if (result.IsFailure)
        {
            Log.Bus.MessageRejected(logger, message.MessageType, message.MessageId.ToString(), handler, result.Error!.DisplayCode);
            throw new MessageRejectedException(result.Error);
        }
    }

    private async Task<Result> RunIdempotentAsync(
        IncomingMessage message, string handler, bool transactional, Func<CancellationToken, Task<Result>> work, CancellationToken cancellationToken)
    {
        if (!transactional)
        {
            if (await processed.IsProcessedAsync(message.MessageId, handler, cancellationToken))
            {
                Log.Bus.DuplicateMessageSkipped(logger, message.MessageId, handler);
                return Result.Success();
            }

            var outcome = await work(cancellationToken);
            if (outcome.IsSuccess)
            {
                await using var mark = await transactions.BeginAsync(cancellationToken);
                await processed.TryMarkProcessedAsync(message.MessageId, handler, cancellationToken);
                await mark.CommitAsync(cancellationToken);
            }

            return outcome;
        }

        // Nested write operations of the handler join this transaction; their post-commit actions run after it.
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        if (!await processed.TryMarkProcessedAsync(message.MessageId, handler, cancellationToken))
        {
            Log.Bus.DuplicateMessageSkipped(logger, message.MessageId, handler);
            return Result.Success();
        }

        var result = await work(cancellationToken);
        if (result.IsSuccess)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return result;
    }

    private void SetCaller(IReadOnlyDictionary<string, string> headers)
    {
        if (currentUser is not MessageCurrentUser caller)
        {
            return;
        }

        var actor = Enum.TryParse<ActorType>(headers.GetValueOrDefault(MessageHeaders.ActorType), out var parsed) && parsed != ActorType.Anonymous
            ? parsed
            : ActorType.System;
        Guid? userId = Guid.TryParse(headers.GetValueOrDefault(MessageHeaders.UserId), out var id) ? id : null;
        caller.Set(actor, actor == ActorType.System ? null : userId);
    }

    private async Task SetTenantAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        if (message.Headers.GetValueOrDefault(MessageHeaders.TenantSlug) is not { Length: > 0 } slug)
        {
            Log.Bus.MessageTenantMissing(logger, message.MessageType, message.MessageId.ToString());
            throw new MessageRejectedException(Errors.Bus.MessageTenantMissing());
        }

        var tenant = await tenants.FindBySlugAsync(slug, cancellationToken);
        if (tenant is not { IsActive: true })
        {
            Log.Bus.MessageTenantUnavailable(logger, slug, message.MessageType, message.MessageId.ToString());
            throw new MessageRejectedException(Errors.Bus.MessageTenantUnavailable(slug));
        }

        tenantContext.Set(tenant);
    }

    private static Activity? StartActivity(IncomingMessage message)
    {
        ActivityContext.TryParse(message.Headers.GetValueOrDefault(MessageHeaders.TraceParent), null, out var parent);
        return AuxiliaInstrumentation.ActivitySource.StartActivity("Bus.Receive " + message.MessageType, ActivityKind.Consumer, parent);
    }
}
