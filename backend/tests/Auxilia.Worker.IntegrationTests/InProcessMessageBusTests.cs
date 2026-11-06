using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Contracts.Messages.V1.Platform;
using Auxilia.Diagnostics;
using Auxilia.Infrastructure.Messaging;
using Auxilia.Persistence.Tenant.Operations;
using Auxilia.SharedKernel.Results;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace Auxilia.Worker.IntegrationTests;

/// <summary>
/// Outbox → queue table of the Catalog → handlers in the same process (<see cref="MessagingMode.InProcess"/>, ADR 0028):
/// the caller of a request stays the HTTP one while handlers see the message caller, retries and the error queue work
/// as with RabbitMQ, and a message written while the process is stopped is handled at the next start.
/// </summary>
[Collection(InProcessBusGroup.Name)]
public sealed class InProcessMessageBusTests(InProcessBusFixture bus)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Outbox_RunsTheJobInTheSameProcessAsTheCallerOfTheRequest()
    {
        var before = Count("inprocess.count");
        await using (var scope = await bus.TenantScopeAsync())
        {
            // Outside a handler the host's own caller is used (HttpCurrentUser in the Api).
            scope.ServiceProvider.GetRequiredService<ICurrentUser>().ShouldBeOfType<InProcessBusFixture.HostUser>();

            var result = await scope.ServiceProvider.GetRequiredService<IOperationRunner>().RunAsync(Operations.Configuration.SetSetting, null, async operation =>
            {
                await scope.ServiceProvider.GetRequiredService<IMessageOutbox>().EnqueueAsync(operation, new RunRecurringJobCommand("inprocess.count"), Ct);
                return Result.Success();
            }, Ct);
            result.IsSuccess.ShouldBeTrue();
        }

        await EventuallyAsync(() => Count("inprocess.count") == before + 1);
        await using var db = bus.TenantDb();
        var entry = await db.Set<OutboxMessage>().OrderByDescending(item => item.CreatedAt).FirstAsync(Ct);
        entry.DispatchedAt.ShouldNotBeNull();
        await EventuallyAsync(async () => await db.Set<ProcessedMessage>().AnyAsync(item => item.MessageId == entry.Id, Ct));
        var run = await db.Set<JobRun>().OrderByDescending(item => item.StartedAt).FirstAsync(item => item.JobCode == "inprocess.count", Ct);
        (run.ActorType, run.ActorId).ShouldBe(("User", InProcessBusFixture.HostUserId));
    }

    [Fact]
    public async Task Handler_SeesTheCallerOfTheMessageNotTheHostOne()
    {
        var before = Count("inprocess.count");
        InProcessBusFixture.JobCallers.Clear();

        await SendAsync(bus.Host.Services, new RunRecurringJobCommand("inprocess.count"), Guid.CreateVersion7());

        await EventuallyAsync(() => Count("inprocess.count") == before + 1);
        InProcessBusFixture.JobCallers.ShouldContain((ActorType.System, (Guid?)null));
        InProcessBusFixture.JobCallers.ShouldNotContain(caller => caller.ActorType == ActorType.User);
    }

    [Fact]
    public async Task TransientFailure_IsRetriedWithADeferredSecondLevelRetry()
    {
        // 2 immediate attempts + a second-level retry (native deferral of the table) with 2 more: the 4th succeeds.
        await SendAsync(bus.Host.Services, new RunRecurringJobCommand("inprocess.flaky"), Guid.CreateVersion7());

        await EventuallyAsync(() => Count("inprocess.flaky") == 4, TimeSpan.FromSeconds(30));
        await using var db = bus.TenantDb();
        await EventuallyAsync(async () => await db.Set<JobRun>().AnyAsync(run => run.JobCode == "inprocess.flaky" && run.Status == JobRunStatus.Succeeded, Ct));
    }

    [Fact]
    public async Task PermanentAndExhaustedFailures_GoToTheErrorQueue()
    {
        var unknownJob = Guid.CreateVersion7();
        var broken = Guid.CreateVersion7();

        await SendAsync(bus.Host.Services, new RunRecurringJobCommand("no.such.job"), unknownJob);
        await SendAsync(bus.Host.Services, new RunRecurringJobCommand("inprocess.broken"), broken);

        await EventuallyAsync(async () => await InErrorQueueAsync(unknownJob) && await InErrorQueueAsync(broken), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task MessageWrittenWhileStopped_IsHandledAtTheNextStart()
    {
        var before = Count("inprocess.count");

        await bus.RestartAsync(async () =>
        {
            // auxctl (or an Api replica that scaled to zero) left a message in the table.
            await using var client = bus.BuildSendOnlyClient();
            await SendAsync(client, new RunRecurringJobCommand("inprocess.count"), Guid.CreateVersion7());
            await Task.Delay(TimeSpan.FromSeconds(1), Ct);
            Count("inprocess.count").ShouldBe(before);
        });

        await EventuallyAsync(() => Count("inprocess.count") == before + 1);
    }

    private static int Count(string job) => InProcessBusFixture.Journal.GetValueOrDefault(job);

    private static Task SendAsync(IServiceProvider services, object message, Guid messageId)
    {
        var headers = new Dictionary<string, string>
        {
            [MessageHeaders.MessageId] = messageId.ToString(),
            [MessageHeaders.ActorType] = "System",
            [MessageHeaders.TenantSlug] = InProcessBusFixture.TenantSlug,
        };
        return services.GetRequiredService<IMessageSender>().SendAsync(message, headers, Ct);
    }

    private async Task<bool> InErrorQueueAsync(Guid messageId)
    {
        await using var connection = new NpgsqlConnection(bus.CatalogConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            $"select count(*) from {MessageBusRegistration.InProcessQueueTable} where recipient = @queue and position(@id in convert_from(headers, 'UTF8')) > 0",
            connection);
        command.Parameters.AddWithValue("queue", MessageRouting.ErrorQueue);
        command.Parameters.AddWithValue("id", messageId.ToString());
        return (long)(await command.ExecuteScalarAsync(Ct))! > 0;
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
