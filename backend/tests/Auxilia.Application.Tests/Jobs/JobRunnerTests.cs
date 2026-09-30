using Auxilia.Application.Abstractions.Jobs;
using Auxilia.Application.Jobs;
using Auxilia.Application.Tests.Platform;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Auxilia.Application.Tests.Jobs;

public sealed class JobRunnerTests
{
    private static readonly Guid RunId = Guid.CreateVersion7();

    private readonly IJobRunStore store = Substitute.For<IJobRunStore>();
    private readonly IRecurringJob job = Substitute.For<IRecurringJob>();
    private readonly IJobLock locks = Substitute.For<IJobLock>();
    private readonly IAsyncDisposable held = Substitute.For<IAsyncDisposable>();
    private readonly JobRunner runner;

    public JobRunnerTests()
    {
        job.Code.Returns("cases.expiry");
        store.StartAsync("cases.expiry", Arg.Any<CancellationToken>()).Returns(RunId);
        locks.TryAcquireAsync("cases.expiry", Arg.Any<CancellationToken>()).Returns(held);
        runner = new JobRunner([job], ManagerHarness.Runner(), store, locks);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Run_Success_RecordsTheRun()
    {
        job.RunAsync(Arg.Any<CancellationToken>()).Returns(Result.Success("3 cases expired"));

        var result = await runner.RunAsync("cases.expiry", Ct);

        result.Value.ShouldBe("3 cases expired");
        await store.Received(1).FinishAsync(RunId, true, null, "3 cases expired", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_Failure_RecordsTheError()
    {
        job.RunAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure<string>(Errors.Jobs.JobRunFailed("cases.expiry")));

        var result = await runner.RunAsync("cases.expiry", Ct);

        result.IsFailure.ShouldBeTrue();
        await store.Received(1).FinishAsync(RunId, false, $"AUX-{EventCodes.Jobs.JobRunFailed}", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_Throws_RecordsAndRethrows()
    {
        job.RunAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));

        await Should.ThrowAsync<InvalidOperationException>(() => runner.RunAsync("cases.expiry", Ct));

        await store.Received(1).FinishAsync(RunId, false, $"AUX-{EventCodes.Host.UnhandledException}", nameof(InvalidOperationException), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_UnknownJob_IsNotFoundAndNotRecorded()
    {
        var result = await runner.RunAsync("unknown", Ct);

        result.Error!.Code.ShouldBe(EventCodes.Jobs.JobNotFound);
        await store.DidNotReceive().StartAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        runner.Jobs.ShouldHaveSingleItem().ShouldBe(job);
    }

    [Fact]
    public async Task Run_LockHeldByAnotherRun_IsRefusedWithoutARunRecord()
    {
        locks.TryAcquireAsync("cases.expiry", Arg.Any<CancellationToken>()).Returns((IAsyncDisposable?)null);

        var result = await runner.RunAsync("cases.expiry", Ct);

        result.Error!.Code.ShouldBe(EventCodes.Jobs.JobAlreadyRunning);
        await store.DidNotReceive().StartAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await job.DidNotReceive().RunAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_ReleasesTheLockAfterTheJob()
    {
        job.RunAsync(Arg.Any<CancellationToken>()).Returns(Result.Success("done"));

        await runner.RunAsync("cases.expiry", Ct);

        await held.Received(1).DisposeAsync();
    }
}
