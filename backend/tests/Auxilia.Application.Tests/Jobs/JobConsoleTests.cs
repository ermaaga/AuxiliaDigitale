using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Jobs;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Jobs;
using Auxilia.Application.Tests.Platform;
using Auxilia.Contracts.Messages.V1.Platform;
using Auxilia.Diagnostics;
using Auxilia.Domain.Platform;

using NSubstitute;

namespace Auxilia.Application.Tests.Jobs;

/// <summary>N02 jobs page: the registered jobs with their last run (who, when, result) and "run now" through the Worker.</summary>
public sealed class JobConsoleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);

    private readonly IJobRunner runner = Substitute.For<IJobRunner>();
    private readonly IJobRunStore store = Substitute.For<IJobRunStore>();
    private readonly IPlatformIdentityStore platformUsers = Substitute.For<IPlatformIdentityStore>();
    private readonly IMessageOutbox outbox = Substitute.For<IMessageOutbox>();
    private readonly PlatformUser ops = new(Guid.CreateVersion7(), "ops@example.test", "Operations");

    public JobConsoleTests()
    {
        IRecurringJob[] jobs = [Job("bus.outbox", "after a message bus outage"), Job("cases.expiry", "daily")];
        runner.Jobs.Returns(jobs);
        platformUsers.ListUsersAsync(Arg.Any<CancellationToken>()).Returns([ops]);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_EveryJobWithItsLastRun_AndTheNameOfThePlatformUser()
    {
        store.RecentAsync("bus.outbox", 1, Arg.Any<CancellationToken>()).Returns([]);
        store.RecentAsync("cases.expiry", 1, Arg.Any<CancellationToken>()).Returns(
            [new JobRunRow(Guid.CreateVersion7(), "cases.expiry", "Running", Now, null, "Platform", ops.Id, null, null)]);

        var jobs = await new JobQueryService(runner, store, platformUsers).ListAsync(Ct);

        jobs.Select(job => (job.Code, job.SuggestedFrequency, job.IsRunning)).ShouldBe(
            [("bus.outbox", "after a message bus outage", false), ("cases.expiry", "daily", true)]);
        jobs[0].LastRun.ShouldBeNull();
        (jobs[1].LastRun!.ActorType, jobs[1].LastRun!.ActorName, jobs[1].LastRun!.StartedAt).ShouldBe(("Platform", "Operations", Now));
    }

    [Fact]
    public async Task Runs_NewestFirst_TakeIsBounded_AuxctlRunsHaveNoName()
    {
        store.RecentAsync(null, IJobQueryService.MaxRuns, Arg.Any<CancellationToken>()).Returns(
            [new JobRunRow(Guid.CreateVersion7(), "cases.expiry", "Failed", Now, Now.AddSeconds(3), "System", null, "AUX-26003", "The job failed")]);

        var runs = await new JobQueryService(runner, store, platformUsers).RunsAsync(10_000, Ct);

        var run = runs.ShouldHaveSingleItem();
        (run.Status, run.ActorType, run.ActorName, run.ErrorCode, run.FinishedAt).ShouldBe(("Failed", "System", null, "AUX-26003", Now.AddSeconds(3)));
        await store.Received(1).RecentAsync(null, IJobQueryService.MaxRuns, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestRun_QueuesTheJobForTheWorker()
    {
        var result = await new JobManager(ManagerHarness.Runner(), runner, outbox).RequestRunAsync("cases.expiry", Ct);

        result.IsSuccess.ShouldBeTrue();
        await outbox.Received(1).EnqueueAsync(
            Arg.Any<IOperationScope>(), Arg.Is<RunRecurringJobCommand>(command => command.JobCode == "cases.expiry"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestRun_UnknownJob_IsNotFoundAndQueuesNothing()
    {
        var result = await new JobManager(ManagerHarness.Runner(), runner, outbox).RequestRunAsync("no.such.job", Ct);

        result.Error!.Code.ShouldBe(EventCodes.Jobs.JobNotFound);
        await outbox.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default!, Ct);
    }

    private static IRecurringJob Job(string code, string frequency)
    {
        var job = Substitute.For<IRecurringJob>();
        job.Code.Returns(code);
        job.SuggestedFrequency.Returns(frequency);
        return job;
    }
}
