using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Bus;
using Auxilia.Application.Execution;
using Auxilia.Application.Tests.Execution;
using Auxilia.Diagnostics;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace Auxilia.Application.Tests.Bus;

public sealed class IncomingMessageProcessorTests
{
    private static readonly TenantInfo Acme = new(Guid.CreateVersion7(), "acme", TenantStatus.Active, "it", "Europe/Rome");
    private static readonly Guid MessageId = Guid.CreateVersion7();
    private static readonly Guid UserId = Guid.CreateVersion7();

    private readonly ITenantDirectory directory = Substitute.For<ITenantDirectory>();
    private readonly ITenantContextSetter tenantSetter = Substitute.For<ITenantContextSetter>();
    private readonly MessageCurrentUser caller = new();
    private readonly CorrelationContext correlation = new();
    private readonly IOperationTransactionFactory transactions = Substitute.For<IOperationTransactionFactory>();
    private readonly IOperationTransaction transaction = Substitute.For<IOperationTransaction>();
    private readonly IProcessedMessageStore processed = Substitute.For<IProcessedMessageStore>();
    private readonly RecordingLogger<IncomingMessageProcessor> logger = new();
    private readonly IncomingMessageProcessor processor;
    private int runs;

    public IncomingMessageProcessorTests()
    {
        directory.FindBySlugAsync("acme", Arg.Any<CancellationToken>()).Returns(Acme);
        directory.FindBySlugAsync("frozen", Arg.Any<CancellationToken>()).Returns(Acme with { Slug = "frozen", Status = TenantStatus.Suspended });
        transactions.BeginAsync(Arg.Any<CancellationToken>()).Returns(transaction);
        processed.TryMarkProcessedAsync(MessageId, "Handler", Arg.Any<CancellationToken>()).Returns(true);

        var runner = new OperationRunner(NullLogger<OperationRunner>.Instance, caller, transactions, [], TimeProvider.System);
        processor = new IncomingMessageProcessor(directory, tenantSetter, caller, correlation, runner, transactions, processed, logger);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TenantMessage_SetsTenantCallerCorrelationAndCommitsWithTheIdempotencyRecord()
    {
        await processor.HandleAsync(Message(new()
        {
            [MessageHeaders.TenantSlug] = "acme",
            [MessageHeaders.CorrelationId] = "corr-1",
            [MessageHeaders.ActorType] = "User",
            [MessageHeaders.UserId] = UserId.ToString(),
            [MessageHeaders.TraceParent] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
        }), "Handler", transactional: true, Work(Result.Success()), Ct);

        runs.ShouldBe(1);
        tenantSetter.Received(1).Set(Acme);
        (caller.ActorType, caller.UserId).ShouldBe((ActorType.User, UserId));
        correlation.CorrelationId.ShouldBe("corr-1");
        await transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicateDelivery_IsAcknowledgedWithoutRunningTheHandler()
    {
        processed.TryMarkProcessedAsync(MessageId, "Handler", Arg.Any<CancellationToken>()).Returns(false);

        await processor.HandleAsync(Message(new() { [MessageHeaders.TenantSlug] = "acme" }), "Handler", transactional: true, Work(Result.Success()), Ct);

        runs.ShouldBe(0);
        logger.Entries.ShouldContain(entry => entry.EventId.Id == EventCodes.Bus.DuplicateMessageSkipped);
        await transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        correlation.CorrelationId.ShouldBe(MessageId.ToString());
        (caller.ActorType, caller.UserId).ShouldBe((ActorType.System, null));
    }

    [Fact]
    public async Task HandlerFailure_RejectsTheMessageAndRollsBack()
    {
        var error = Errors.Jobs.JobNotFound("x");

        var rejected = await Should.ThrowAsync<MessageRejectedException>(() =>
            processor.HandleAsync(Message(new() { [MessageHeaders.TenantSlug] = "acme" }), "Handler", transactional: true, Work(Result.Failure(error)), Ct));

        rejected.Error.ShouldBe(error);
        await transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        logger.Entries.ShouldContain(entry => entry.EventId.Id == EventCodes.Bus.MessageRejected);
    }

    [Theory]
    [InlineData(null, EventCodes.Bus.MessageTenantMissing)]
    [InlineData("unknown", EventCodes.Bus.MessageTenantUnavailable)]
    [InlineData("frozen", EventCodes.Bus.MessageTenantUnavailable)]
    public async Task TenantMessage_WithoutAnActiveTenant_IsRejected(string? slug, int code)
    {
        var headers = new Dictionary<string, string>();
        if (slug is not null)
        {
            headers[MessageHeaders.TenantSlug] = slug;
        }

        var rejected = await Should.ThrowAsync<MessageRejectedException>(() =>
            processor.HandleAsync(Message(headers), "Handler", transactional: true, Work(Result.Success()), Ct));

        rejected.Error.Code.ShouldBe(code);
        runs.ShouldBe(0);
    }

    [Fact]
    public async Task NonTransactionalHandler_IsMarkedAfterSuccessAndSkippedWhenAlreadyDone()
    {
        var message = Message(new() { [MessageHeaders.TenantSlug] = "acme", [MessageHeaders.ActorType] = "System", [MessageHeaders.UserId] = UserId.ToString() });

        await processor.HandleAsync(message, "Handler", transactional: false, Work(Result.Success()), Ct);
        processed.IsProcessedAsync(MessageId, "Handler", Arg.Any<CancellationToken>()).Returns(true);
        await processor.HandleAsync(message, "Handler", transactional: false, Work(Result.Success()), Ct);

        runs.ShouldBe(1);
        await processed.Received(1).TryMarkProcessedAsync(MessageId, "Handler", Arg.Any<CancellationToken>());
        (caller.ActorType, caller.UserId).ShouldBe((ActorType.System, null));
    }

    [Fact]
    public async Task NonTransactionalHandler_Failure_IsNotMarked()
    {
        await Should.ThrowAsync<MessageRejectedException>(() => processor.HandleAsync(
            Message(new() { [MessageHeaders.TenantSlug] = "acme" }), "Handler", transactional: false, Work(Result.Failure(Errors.Host.Unexpected())), Ct));

        await processed.DidNotReceive().TryMarkProcessedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlatformMessage_RunsWithoutTenantOrIdempotencyRecord()
    {
        await processor.HandleAsync(Message(new(), isTenantMessage: false), "Handler", transactional: true, Work(Result.Success()), Ct);

        runs.ShouldBe(1);
        tenantSetter.DidNotReceive().Set(Arg.Any<TenantInfo>());
        await processed.DidNotReceive().TryMarkProcessedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RejectedException_HasErrorForEveryConstructor()
    {
        new MessageRejectedException().Error.Code.ShouldBe(EventCodes.Host.UnhandledException);
        new MessageRejectedException("x").Error.Code.ShouldBe(EventCodes.Host.UnhandledException);
        new MessageRejectedException("x", new InvalidOperationException()).InnerException.ShouldNotBeNull();
    }

    private static IncomingMessage Message(Dictionary<string, string> headers, bool isTenantMessage = true) =>
        new(MessageId, "TestMessage", isTenantMessage, headers);

    private Func<CancellationToken, Task<Result>> Work(Result result) => _ =>
    {
        runs++;
        return Task.FromResult(result);
    };
}
