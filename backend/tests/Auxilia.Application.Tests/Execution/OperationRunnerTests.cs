using System.Diagnostics;
using System.Diagnostics.Metrics;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Execution;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging;

using NSubstitute;

namespace Auxilia.Application.Tests.Execution;

public sealed class OperationRunnerTests : IDisposable
{
    // Codes of the Host range stand in for a module success code: the runner does not care which range.
    private static readonly OperationDescriptor Write = new("Test.Write", EventCodes.Host.LogStorageRecovered);
    private static readonly OperationDescriptor Read = new("Test.Read", EventCodes.Host.LogStorageRecovered, isWrite: false);
    private static readonly Error Expected = Error.NotFound(14003, "Case not found");

    private readonly RecordingLogger<OperationRunner> logger = new();
    private readonly ICurrentUser user = Substitute.For<ICurrentUser>();
    private readonly IOperationTransactionFactory transactions = Substitute.For<IOperationTransactionFactory>();
    private readonly IOperationTransaction transaction = Substitute.For<IOperationTransaction>();
    private readonly IExceptionClassifier classifier = Substitute.For<IExceptionClassifier>();
    private readonly List<Activity> activities = [];
    private readonly ActivityListener listener;
    private readonly OperationRunner runner;

    public OperationRunnerTests()
    {
        user.ActorType.Returns(ActorType.User);
        user.UserId.Returns(Guid.Parse("0199a0b2-0000-7000-8000-000000000001"));
        transactions.BeginAsync(Arg.Any<CancellationToken>()).Returns(transaction);

        listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);

        runner = new OperationRunner(logger, user, transactions, [classifier], TimeProvider.System);
    }

    [Fact]
    public async Task RunAsync_Success_CommitsAndLogsSuccessCode()
    {
        var result = await runner.RunAsync(Write, null, _ => Task.FromResult(Result.Success(42)), TestContext.Current.CancellationToken);

        result.Value.ShouldBe(42);
        await transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.EventId.Id.ShouldBe(EventCodes.Host.LogStorageRecovered);
        entry.EventId.Name.ShouldBe("Host.LogStorageRecovered");
        entry.Properties["Operation"].ShouldBe("Test.Write");
        entry.Properties["{OriginalFormat}"].ShouldBe(OperationLog.SucceededTemplate);
    }

    [Fact]
    public async Task RunAsync_ExpectedFailure_DoesNotCommitAndLogsWarningWithErrorCode()
    {
        var result = await runner.RunAsync(Write, null, _ => Task.FromResult(Result.Failure<int>(Expected)), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(Expected);
        await transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await transaction.Received(1).DisposeAsync();
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.EventId.Id.ShouldBe(14003);
        entry.Properties["ErrorCode"].ShouldBe("AUX-14003");
    }

    [Fact]
    public async Task RunAsync_ReadOperation_OpensNoTransaction()
    {
        await runner.RunAsync(Read, null, _ => Task.FromResult(Result.Success()), TestContext.Current.CancellationToken);

        await transactions.DidNotReceive().BeginAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_KnownException_ReturnsItsErrorAndLogsWarning()
    {
        var concurrency = new InvalidOperationException("row version changed");
        classifier.Classify(concurrency).Returns(Errors.Host.ConcurrencyConflict());

        var result = await runner.RunAsync(Write, null, Task<Result> (_) => throw concurrency, TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe(EventCodes.Host.ConcurrencyConflict);
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBe(concurrency);
    }

    [Fact]
    public async Task RunAsync_UnexpectedException_LogsErrorOnceMarksAndRethrows()
    {
        var bug = new InvalidOperationException("bug");

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => runner.RunAsync(Write, null, Task<Result> (_) => throw bug, TestContext.Current.CancellationToken));

        thrown.ShouldBeSameAs(bug);
        ExceptionLogging.IsLogged(bug).ShouldBeTrue();
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.EventId.Id.ShouldBe(EventCodes.Host.UnhandledException);
    }

    [Fact]
    public async Task RunAsync_Timeout_LogsDatabaseTimeoutCode()
    {
        var timeout = new InvalidOperationException("query failed", new TimeoutException());

        await Should.ThrowAsync<InvalidOperationException>(
            () => runner.RunAsync(Write, null, Task<Result> (_) => throw timeout, TestContext.Current.CancellationToken));

        logger.Entries.ShouldHaveSingleItem().EventId.Id.ShouldBe(EventCodes.Host.DatabaseTimeout);
    }

    [Fact]
    public async Task RunAsync_CallerCancels_RethrowsWithoutLogging()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => runner.RunAsync(
            Read, null, Task<Result> (_) => throw new OperationCanceledException(cancellation.Token), cancellation.Token));

        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_ScopeCarriesOperationActorContextAndEntity()
    {
        var caseId = Guid.CreateVersion7();
        var clientId = Guid.CreateVersion7();

        await runner.RunAsync(Write, new { ClientId = clientId }, scope =>
        {
            scope.SetEntity("Case", caseId);
            return Task.FromResult(Result.Success());
        }, TestContext.Current.CancellationToken);

        var scope = logger.Entries.ShouldHaveSingleItem().Scope;
        scope["Operation"].ShouldBe("Test.Write");
        scope["ActorType"].ShouldBe("User");
        scope["UserId"].ShouldBe(user.UserId);
        scope["ClientId"].ShouldBe(clientId);
        scope["CaseId"].ShouldBe(caseId);
    }

    [Fact]
    public async Task RunAsync_StartsActivityNamedAfterOperation()
    {
        await runner.RunAsync(Read, null, _ => Task.FromResult(Result.Failure(Expected)), TestContext.Current.CancellationToken);

        var activity = activities.ShouldHaveSingleItem(a => a.OperationName == "Test.Read");
        activity.GetTagItem("auxilia.outcome").ShouldBe("failure");
        activity.GetTagItem("auxilia.error_code").ShouldBe("AUX-14003");
    }

    [Fact]
    public async Task RunAsync_RecordsCountWithOperationAndOutcome()
    {
        var outcomes = new List<string>();
        using var meters = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == Telemetry.MeterName && instrument.Name == AuxiliaInstrumentation.CountMetric)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        meters.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var values = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value);
            if (Equals(values["operation"], "Test.Read"))
            {
                outcomes.Add((string)values["outcome"]!);
            }
        });
        meters.Start();

        await runner.RunAsync(Read, null, _ => Task.FromResult(Result.Success()), TestContext.Current.CancellationToken);

        outcomes.ShouldContain("success");
    }

    [Fact]
    public async Task OnCommitted_RunsAfterTheCommitOfASuccessfulOperation()
    {
        var order = new List<string>();
        transaction.When(item => item.CommitAsync(Arg.Any<CancellationToken>())).Do(_ => order.Add("commit"));

        await runner.RunAsync(Write, null, scope =>
        {
            scope.OnCommitted(_ => { order.Add("action"); return Task.CompletedTask; });
            return Task.FromResult(Result.Success());
        }, TestContext.Current.CancellationToken);

        order.ShouldBe(["commit", "action"]);
    }

    [Fact]
    public async Task OnCommitted_IsDiscardedWhenTheOperationFails()
    {
        var ran = false;

        await runner.RunAsync(Write, null, scope =>
        {
            scope.OnCommitted(_ => { ran = true; return Task.CompletedTask; });
            return Task.FromResult(Result.Failure(Expected));
        }, TestContext.Current.CancellationToken);

        // A later successful operation of the same scope does not run the discarded action.
        await runner.RunAsync(Write, null, _ => Task.FromResult(Result.Success()), TestContext.Current.CancellationToken);

        ran.ShouldBeFalse();
    }

    [Fact]
    public async Task OnCommitted_InNestedOperation_RunsOnceWhenTheOutermostSucceeds()
    {
        var runs = 0;
        var ranBeforeOuterEnded = false;

        await runner.RunAsync(Write, null, async _ =>
        {
            await runner.RunAsync(Write, null, inner =>
            {
                inner.OnCommitted(_ => { runs++; return Task.CompletedTask; });
                return Task.FromResult(Result.Success());
            }, TestContext.Current.CancellationToken);

            ranBeforeOuterEnded = runs > 0;
            return Result.Success();
        }, TestContext.Current.CancellationToken);

        ranBeforeOuterEnded.ShouldBeFalse();
        runs.ShouldBe(1);
    }

    [Fact]
    public async Task OnCommitted_FailingAction_IsLoggedAndTheResultStaysSuccessful()
    {
        var result = await runner.RunAsync(Write, null, scope =>
        {
            scope.OnCommitted(_ => throw new InvalidOperationException("cache down"));
            return Task.FromResult(Result.Success());
        }, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        logger.Entries.ShouldContain(entry => entry.EventId.Id == EventCodes.Host.PostCommitActionFailed && entry.Level == LogLevel.Warning);
        logger.Entries.ShouldContain(entry => entry.EventId.Id == EventCodes.Host.LogStorageRecovered);
    }

    public void Dispose() => listener.Dispose();
}

file static class ShouldlyActivityExtensions
{
    public static Activity ShouldHaveSingleItem(this List<Activity> activities, Func<Activity, bool> predicate) =>
        activities.Where(predicate).ToList().ShouldHaveSingleItem();
}
