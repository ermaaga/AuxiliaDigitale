using System.Diagnostics;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Bus;
using Auxilia.Application.Tests.Execution;
using Auxilia.Contracts.Messages.V1.Platform;
using Auxilia.Diagnostics;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Auxilia.Application.Tests.Bus;

public sealed class OutboxTests
{
    private static readonly TenantInfo Acme = new(Guid.CreateVersion7(), "acme", TenantStatus.Active, "it", "Europe/Rome");
    private static readonly Guid UserId = Guid.CreateVersion7();

    private readonly List<OutboxEntry> entries = [];
    private readonly HashSet<Guid> dispatched = [];
    private readonly IOutboxStore store = Substitute.For<IOutboxStore>();
    private readonly IMessageSender sender = Substitute.For<IMessageSender>();
    private readonly RecordingLogger<OutboxDispatcher> logger = new();
    private readonly ITenantContext tenantContext = Substitute.For<ITenantContext>();
    private readonly ICurrentUser user = Substitute.For<ICurrentUser>();

    public OutboxTests()
    {
        store.When(item => item.AddAsync(Arg.Any<OutboxEntry>(), Arg.Any<CancellationToken>())).Do(call => entries.Add(call.Arg<OutboxEntry>()));
        store.GetPendingAsync(Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var ids = call.ArgAt<IReadOnlyCollection<Guid>?>(0);
                var before = call.ArgAt<DateTimeOffset>(1);
                return entries.Where(entry => !dispatched.Contains(entry.Id) && (ids?.Contains(entry.Id) ?? entry.CreatedAt <= before)).ToList();
            });
        store.When(item => item.MarkDispatchedAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()))
            .Do(call => dispatched.Add(call.Arg<Guid>()));
        tenantContext.Current.Returns(Acme);
        user.ActorType.Returns(ActorType.User);
        user.UserId.Returns(UserId);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Enqueue_StoresTheMessageWithHeadersAndSendsItAfterTheCommit()
    {
        var correlation = new CorrelationContext();
        correlation.Set("corr-1");
        IOperationScope? captured = null;
        using var activity = new Activity("request").Start();

        var result = await ManagerHarness().RunAsync(Operations.Configuration.SetSetting, null, async scope =>
        {
            captured = scope;
            await Outbox(correlation).EnqueueAsync(scope, new RunRecurringJobCommand("cases.expiry"), Ct);
            await sender.DidNotReceive().SendAsync(Arg.Any<object>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
            return Result.Success();
        }, Ct);

        result.IsSuccess.ShouldBeTrue();
        var entry = entries.ShouldHaveSingleItem();
        entry.MessageType.ShouldBe(typeof(RunRecurringJobCommand).FullName);
        entry.Body.ShouldBe("""{"jobCode":"cases.expiry"}""");
        entry.Headers[MessageHeaders.MessageId].ShouldBe(entry.Id.ToString());
        entry.Headers[MessageHeaders.TenantSlug].ShouldBe("acme");
        entry.Headers[MessageHeaders.CorrelationId].ShouldBe("corr-1");
        entry.Headers[MessageHeaders.UserId].ShouldBe(UserId.ToString());
        entry.Headers[MessageHeaders.ActorType].ShouldBe("User");
        // The runner may start a child activity (when listened to): same trace either way.
        entry.Headers[MessageHeaders.TraceParent].ShouldStartWith($"00-{activity.TraceId}-");
        await sender.Received(1).SendAsync(
            Arg.Is<object>(message => ((RunRecurringJobCommand)message).JobCode == "cases.expiry"),
            Arg.Is<IReadOnlyDictionary<string, string>>(headers => headers[MessageHeaders.TenantSlug] == "acme"),
            Arg.Any<CancellationToken>());
        dispatched.ShouldContain(entry.Id);
    }

    [Fact]
    public async Task Enqueue_OperationFails_NothingIsSent()
    {
        await ManagerHarness().RunAsync(Operations.Configuration.SetSetting, null, async scope =>
        {
            await Outbox(new CorrelationContext()).EnqueueAsync(scope, new RunRecurringJobCommand("cases.expiry"), Ct);
            return Result.Failure(Errors.Host.Unexpected());
        }, Ct);

        await sender.DidNotReceive().SendAsync(Arg.Any<object>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Enqueue_RejectsObjectsThatAreNotContractMessages()
    {
        var scope = Substitute.For<IOperationScope>();

        await Should.ThrowAsync<ArgumentException>(() => Outbox(new CorrelationContext()).EnqueueAsync(scope, "text", Ct));
    }

    [Fact]
    public async Task Dispatch_SendFailure_LeavesTheEntryPendingForTheJob()
    {
        var old = Entry(DateTimeOffset.UtcNow.AddMinutes(-10));
        var recent = Entry(DateTimeOffset.UtcNow);
        entries.AddRange([old, recent]);
        sender.SendAsync(Arg.Any<object>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("bus down")), Task.CompletedTask);
        var job = new OutboxDispatchJob(Dispatcher());

        (await job.RunAsync(Ct)).Error!.Code.ShouldBe(EventCodes.Jobs.JobRunFailed);
        dispatched.ShouldBeEmpty();
        logger.Entries.ShouldHaveSingleItem().EventId.Id.ShouldBe(EventCodes.Bus.OutboxDispatchFailed);

        // Entries younger than a minute are left to the operation that is sending them.
        (await job.RunAsync(Ct)).Value.ShouldBe("sent 1");
        dispatched.ShouldBe([old.Id]);
        (job.Code, job.Description.Length > 0, job.SuggestedFrequency.Length > 0).ShouldBe(("bus.outbox", true, true));
    }

    [Fact]
    public void CorrelationContext_DefaultsToTheTraceIdAndStaysStable()
    {
        using var activity = new Activity("request").Start();
        var correlation = new CorrelationContext();

        correlation.CorrelationId.ShouldBe(activity.TraceId.ToString());
        activity.Stop();
        correlation.CorrelationId.ShouldBe(activity.TraceId.ToString());
        Should.Throw<ArgumentException>(() => correlation.Set(" "));
    }

    [Fact]
    public void Headers_WithoutTenantUserOrTrace_CarryOnlyIdCorrelationAndActor()
    {
        tenantContext.Current.Returns((TenantInfo?)null);
        user.ActorType.Returns(ActorType.System);
        user.UserId.Returns((Guid?)null);
        Activity.Current = null;

        var headers = new OutgoingMessageHeaders(tenantContext, user, new CorrelationContext()).Create(Guid.CreateVersion7());

        headers.Keys.ShouldBe([MessageHeaders.MessageId, MessageHeaders.CorrelationId, MessageHeaders.ActorType], ignoreOrder: true);
    }

    [Fact]
    public void MessageTypes_RoundTripContractsOnly()
    {
        var body = MessageTypes.Serialize(new RunRecurringJobCommand("x"));

        MessageTypes.Deserialize(typeof(RunRecurringJobCommand).FullName!, body).ShouldBe(new RunRecurringJobCommand("x"));
        Should.Throw<InvalidOperationException>(() => MessageTypes.Deserialize("System.String", "\"x\""));
        Should.Throw<InvalidOperationException>(() => MessageTypes.Deserialize("Auxilia.Contracts.Messages.V1.Nope", "{}"));
    }

    private static Application.Execution.OperationRunner ManagerHarness() => Platform.ManagerHarness.Runner();

    private MessageOutbox Outbox(CorrelationContext correlation) =>
        new(store, Dispatcher(), new OutgoingMessageHeaders(tenantContext, user, correlation), TimeProvider.System);

    private OutboxDispatcher Dispatcher() => new(store, sender, TimeProvider.System, logger);

    private static OutboxEntry Entry(DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), typeof(RunRecurringJobCommand).FullName!, """{"jobCode":"x"}""", new Dictionary<string, string>(), createdAt);
}
