using System.Text;
using System.Text.Json;

using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Contracts.Messages.V1.Platform;
using Auxilia.Diagnostics;
using Auxilia.Infrastructure.Messaging;
using Auxilia.Persistence.Tenant.Operations;
using Auxilia.SharedKernel.Results;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using RabbitMQ.Client;

namespace Auxilia.Worker.IntegrationTests;

/// <summary>
/// Outbox → RabbitMQ → Worker (skill auxilia-messaging-rebus, step 6): happy path, duplicate delivery, transient
/// failure retried, permanent failure and missing tenant to the error queue, second-level retries exhausted.
/// </summary>
[Collection(BusGroup.Name)]
public sealed class MessageBusTests(BusFixture bus)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Outbox_SendsAfterCommitAndTheWorkerRunsTheJobOnce()
    {
        var before = Count("test.count");
        Guid outboxId;
        await using (var scope = await bus.TenantScopeAsync())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IOperationRunner>().RunAsync(Operations.Configuration.SetSetting, null, async operation =>
            {
                await scope.ServiceProvider.GetRequiredService<IMessageOutbox>().EnqueueAsync(operation, new RunRecurringJobCommand("test.count"), Ct);
                return Result.Success();
            }, Ct);
            result.IsSuccess.ShouldBeTrue();
        }

        await EventuallyAsync(() => Count("test.count") == before + 1);
        await using var db = bus.TenantDb();
        var entry = await db.Set<OutboxMessage>().OrderByDescending(item => item.CreatedAt).FirstAsync(Ct);
        outboxId = entry.Id;
        entry.DispatchedAt.ShouldNotBeNull();
        await EventuallyAsync(async () => await db.Set<JobRun>().AnyAsync(run => run.JobCode == "test.count" && run.Status == JobRunStatus.Succeeded, Ct));
        var run = await db.Set<JobRun>().FirstAsync(item => item.JobCode == "test.count", Ct);
        (run.ActorType, run.ActorId).ShouldBe(("User", Guid.Parse("0199a0b2-0000-7000-8000-00000000b055")));
        await EventuallyAsync(async () => await db.Set<ProcessedMessage>().AnyAsync(item => item.MessageId == outboxId, Ct));
    }

    [Fact]
    public async Task DuplicateDelivery_IsHandledOnce()
    {
        var before = Count("test.count");
        var messageId = Guid.CreateVersion7();

        await SendAsync(new RunRecurringJobCommand("test.count"), messageId);
        await EventuallyAsync(() => Count("test.count") == before + 1);
        await using (var db = bus.TenantDb())
        {
            await EventuallyAsync(async () => await db.Set<ProcessedMessage>().AnyAsync(item => item.MessageId == messageId, Ct));
        }

        await SendAsync(new RunRecurringJobCommand("test.count"), messageId);
        await Task.Delay(TimeSpan.FromSeconds(2), Ct);

        Count("test.count").ShouldBe(before + 1);
    }

    [Fact]
    public async Task TransientFailure_IsRetriedUntilItSucceeds()
    {
        // 2 immediate attempts + a second-level retry with 2 more: the 4th attempt succeeds.
        await SendAsync(new RunRecurringJobCommand("test.flaky"), Guid.CreateVersion7());

        await EventuallyAsync(() => Count("test.flaky") == 4, TimeSpan.FromSeconds(30));
        await using var db = bus.TenantDb();
        await EventuallyAsync(async () => await db.Set<JobRun>().AnyAsync(run => run.JobCode == "test.flaky" && run.Status == JobRunStatus.Succeeded, Ct));
        (await db.Set<JobRun>().CountAsync(run => run.JobCode == "test.flaky" && run.Status == JobRunStatus.Failed, Ct)).ShouldBe(3);
    }

    [Fact]
    public async Task PermanentFailures_GoToTheErrorQueueWithoutRetries()
    {
        var unknownJob = Guid.CreateVersion7();
        var noTenant = Guid.CreateVersion7();

        await SendAsync(new RunRecurringJobCommand("no.such.job"), unknownJob);
        await SendAsync(new RunRecurringJobCommand("test.count"), noTenant, tenant: null);

        var deadLettered = await ErrorQueueIdsAsync([unknownJob, noTenant]);
        deadLettered.ShouldBe([unknownJob, noTenant], ignoreOrder: true);
    }

    [Fact]
    public async Task ExhaustedRetries_GoToTheErrorQueue()
    {
        var before = Count("test.broken");
        var messageId = Guid.CreateVersion7();

        await SendAsync(new RunRecurringJobCommand("test.broken"), messageId);

        (await ErrorQueueIdsAsync([messageId], TimeSpan.FromSeconds(30))).ShouldBe([messageId]);
        Count("test.broken").ShouldBe(before + 4);
    }

    [Fact]
    public void Routing_MapsMessagesToTheQueueOfTheirModule()
    {
        MessageRouting.QueueOf(typeof(RunRecurringJobCommand)).ShouldBe(MessageRouting.PlatformQueue);
        MessageRouting.MessageTypes.ShouldContain(typeof(RunRecurringJobCommand));
        MessageRouting.QueuesHandledBy(typeof(WorkerServices).Assembly).ShouldBe(["auxilia.documents", "auxilia.messaging", MessageRouting.PlatformQueue]);
        Should.Throw<ArgumentException>(() => MessageRouting.QueueOf(typeof(string)));
    }

    private static int Count(string job) => BusFixture.Journal.GetValueOrDefault(job);

    private async Task SendAsync(object message, Guid messageId, string? tenant = BusFixture.TenantSlug)
    {
        var headers = new Dictionary<string, string>
        {
            [MessageHeaders.MessageId] = messageId.ToString(),
            [MessageHeaders.ActorType] = "System",
        };
        if (tenant is not null)
        {
            headers[MessageHeaders.TenantSlug] = tenant;
        }

        await bus.Api.GetRequiredService<IMessageSender>().SendAsync(message, headers, Ct);
    }

    /// <summary>Reads the error queue until the expected message ids arrived (messages of other tests are put back).</summary>
    private async Task<IReadOnlyList<Guid>> ErrorQueueIdsAsync(IReadOnlyCollection<Guid> expected, TimeSpan? timeout = null)
    {
        var factory = new ConnectionFactory { Uri = new Uri(bus.RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync(Ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        var found = new List<Guid>();
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));

        while (found.Count < expected.Count && DateTime.UtcNow < deadline)
        {
            var delivery = await channel.BasicGetAsync(MessageRouting.ErrorQueue, autoAck: false, Ct);
            if (delivery is null)
            {
                await Task.Delay(200, Ct);
                continue;
            }

            var id = delivery.BasicProperties.Headers?.TryGetValue(MessageHeaders.MessageId, out var raw) == true && raw is byte[] bytes
                ? Guid.Parse(Encoding.UTF8.GetString(bytes))
                : Guid.Empty;
            if (expected.Contains(id))
            {
                found.Add(id);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, Ct);
            }
            else
            {
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, Ct);
            }
        }

        return found;
    }

    private static Task EventuallyAsync(Func<bool> condition, TimeSpan? timeout = null) =>
        EventuallyAsync(() => Task.FromResult(condition()), timeout);

    private static async Task EventuallyAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met in time.");
            }

            await Task.Delay(100, Ct);
        }
    }
}
