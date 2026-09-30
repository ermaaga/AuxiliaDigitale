using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Execution;

/// <inheritdoc cref="IOperationRunner"/>
internal sealed class OperationRunner : IOperationRunner
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ContextProperties = new();

    private readonly ILogger<OperationRunner> logger;
    private readonly ICurrentUser currentUser;
    private readonly IOperationTransactionFactory transactions;
    private readonly IEnumerable<IExceptionClassifier> classifiers;
    private readonly TimeProvider timeProvider;
    private readonly List<Func<CancellationToken, Task>> committedActions = [];
    private int depth;

    public OperationRunner(
        ILogger<OperationRunner> logger,
        ICurrentUser currentUser,
        IOperationTransactionFactory transactions,
        IEnumerable<IExceptionClassifier> classifiers,
        TimeProvider timeProvider)
    {
        this.logger = logger;
        this.currentUser = currentUser;
        this.transactions = transactions;
        this.classifiers = classifiers;
        this.timeProvider = timeProvider;
    }

    public Task<Result> RunAsync(
        OperationDescriptor operation, object? context, Func<IOperationScope, Task<Result>> work, CancellationToken cancellationToken) =>
        RunCoreAsync(operation, context, work, Result.Failure, cancellationToken);

    public Task<Result<TValue>> RunAsync<TValue>(
        OperationDescriptor operation, object? context, Func<IOperationScope, Task<Result<TValue>>> work, CancellationToken cancellationToken) =>
        RunCoreAsync(operation, context, work, Result.Failure<TValue>, cancellationToken);

    private async Task<TResult> RunCoreAsync<TResult>(
        OperationDescriptor operation,
        object? context,
        Func<IOperationScope, Task<TResult>> work,
        Func<Error, TResult> failure,
        CancellationToken cancellationToken)
        where TResult : Result
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(work);

        var scope = new Scope(operation, committedActions);
        scope.Properties["Operation"] = operation.Name;
        scope.Properties["ActorType"] = currentUser.ActorType.ToString();
        if (currentUser.UserId is { } userId)
        {
            scope.Properties["UserId"] = userId;
        }

        AddContext(scope, context);

        using var activity = AuxiliaInstrumentation.ActivitySource.StartActivity(operation.Name);
        scope.Activity = activity;
        activity?.SetTag("auxilia.operation", operation.Name);
        activity?.SetTag("auxilia.actor_type", currentUser.ActorType.ToString());
        foreach (var (key, value) in scope.Properties)
        {
            activity?.SetTag("auxilia." + key, value);
        }

        using var logScope = logger.BeginScope(scope.Properties);
        var started = timeProvider.GetTimestamp();
        var isOutermost = depth++ == 0;

        try
        {
            var result = operation.IsWrite
                ? await RunInTransactionAsync(scope, work, cancellationToken)
                : await work(scope);

            if (isOutermost && result.IsSuccess)
            {
                await RunCommittedActionsAsync(operation);
            }

            var elapsed = timeProvider.GetElapsedTime(started);
            if (result.IsSuccess)
            {
                OperationLog.Succeeded(logger, EventIdOf(operation.SuccessCode), operation.Name, elapsed.TotalMilliseconds);
                Record(operation, OperationOutcome.Success, elapsed, activity);
            }
            else
            {
                var error = result.Error!;
                OperationLog.Failed(logger, EventIdOf(error.Code), operation.Name, error.DisplayCode, error.Description, exception: null);
                Record(operation, OperationOutcome.Failure, elapsed, activity, error.DisplayCode);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller gave up: not an error (the host logs the cancelled request).
            Record(operation, OperationOutcome.Cancelled, timeProvider.GetElapsedTime(started), activity);
            throw;
        }
        catch (Exception exception) when (Classify(exception) is { } error)
        {
            OperationLog.Failed(logger, EventIdOf(error.Code), operation.Name, error.DisplayCode, error.Description, exception);
            Record(operation, OperationOutcome.Failure, timeProvider.GetElapsedTime(started), activity, error.DisplayCode);
            return failure(error);
        }
        catch (Exception exception)
        {
            var code = IsTimeout(exception) ? EventCodes.Host.DatabaseTimeout : EventCodes.Host.UnhandledException;
            OperationLog.Error(logger, EventIdOf(code), operation.Name, $"AUX-{code}", exception);
            ExceptionLogging.MarkLogged(exception);
            Record(operation, OperationOutcome.Error, timeProvider.GetElapsedTime(started), activity, $"AUX-{code}");
            throw;
        }
        finally
        {
            depth--;
            if (isOutermost)
            {
                committedActions.Clear();
            }
        }
    }

    /// <summary>The changes are committed: the actions run even if the caller has given up meanwhile.</summary>
    private async Task RunCommittedActionsAsync(OperationDescriptor operation)
    {
        foreach (var action in committedActions.ToArray())
        {
            try
            {
                await action(CancellationToken.None);
            }
            catch (Exception exception)
            {
                Log.Host.PostCommitActionFailed(logger, exception, operation.Name);
            }
        }
    }

    private async Task<TResult> RunInTransactionAsync<TResult>(
        Scope scope, Func<IOperationScope, Task<TResult>> work, CancellationToken cancellationToken)
        where TResult : Result
    {
        await using var transaction = await transactions.BeginAsync(cancellationToken);

        var result = await work(scope);
        if (result.IsSuccess)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return result;
    }

    private Error? Classify(Exception exception)
    {
        foreach (var classifier in classifiers)
        {
            if (classifier.Classify(exception) is { } error)
            {
                return error;
            }
        }

        return null;
    }

    private static bool IsTimeout(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException)
            {
                return true;
            }
        }

        return false;
    }

    private static void AddContext(Scope scope, object? context)
    {
        if (context is null)
        {
            return;
        }

        var properties = ContextProperties.GetOrAdd(
            context.GetType(),
            type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.GetIndexParameters().Length == 0).ToArray());

        foreach (var property in properties)
        {
            scope.Properties[property.Name] = property.GetValue(context);
        }
    }

    private static EventId EventIdOf(int code) => new(code, EventRegistry.EventNameOf(code));

    private static void Record(OperationDescriptor operation, string outcome, TimeSpan elapsed, Activity? activity, string? errorCode = null)
    {
        var tags = new TagList { { "operation", operation.Name }, { "outcome", outcome } };
        AuxiliaInstrumentation.Duration.Record(elapsed.TotalSeconds, tags);
        AuxiliaInstrumentation.Count.Add(1, tags);

        if (activity is null)
        {
            return;
        }

        activity.SetTag("auxilia.outcome", outcome);
        if (errorCode is not null)
        {
            activity.SetTag("auxilia.error_code", errorCode);
        }

        if (outcome == OperationOutcome.Error)
        {
            activity.SetStatus(ActivityStatusCode.Error, errorCode);
        }
    }

    private sealed class Scope(OperationDescriptor operation, List<Func<CancellationToken, Task>> committedActions) : IOperationScope
    {
        public OperationDescriptor Operation { get; } = operation;

        /// <summary>Log scope state; entities added during the work appear in every later log line.</summary>
        public Dictionary<string, object?> Properties { get; } = new(StringComparer.Ordinal);

        public Activity? Activity { get; set; }

        public void SetEntity(string entityType, Guid id)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(entityType);

            Properties[entityType + "Id"] = id;
            Activity?.SetTag($"auxilia.{entityType.ToLowerInvariant()}_id", id);
        }

        public void OnCommitted(Func<CancellationToken, Task> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            committedActions.Add(action);
        }
    }
}
