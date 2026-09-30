using Auxilia.Application.Abstractions.Jobs;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Jobs;

/// <summary>Runs a registered recurring job by hand for the tenant of the current scope (D-15, ADR 0007).</summary>
public interface IJobRunner
{
    IReadOnlyList<IRecurringJob> Jobs { get; }

    Task<Result<string>> RunAsync(string jobCode, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IJobRunner"/>
internal sealed class JobRunner : IJobRunner
{
    private readonly IOperationRunner operations;
    private readonly IJobRunStore runs;
    private readonly IJobLock locks;

    public JobRunner(IEnumerable<IRecurringJob> jobs, IOperationRunner operations, IJobRunStore runs, IJobLock locks)
    {
        Jobs = jobs.OrderBy(job => job.Code, StringComparer.Ordinal).ToArray();
        this.operations = operations;
        this.runs = runs;
        this.locks = locks;
    }

    public IReadOnlyList<IRecurringJob> Jobs { get; }

    // Not a write operation: the run row must survive a failed job; the job's own changes go through Managers.
    // One run at a time per tenant and job (IJobLock); a refused run leaves no ops.job_runs row.
    public Task<Result<string>> RunAsync(string jobCode, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Jobs.RunJob, new { JobCode = jobCode }, async scope =>
        {
            var job = Jobs.FirstOrDefault(item => string.Equals(item.Code, jobCode, StringComparison.Ordinal));
            if (job is null)
            {
                return Errors.Jobs.JobNotFound(jobCode);
            }

            await using var held = await locks.TryAcquireAsync(job.Code, cancellationToken);
            if (held is null)
            {
                return Errors.Jobs.JobAlreadyRunning(job.Code);
            }

            var runId = await runs.StartAsync(job.Code, cancellationToken);
            scope.SetEntity("JobRun", runId);

            Result<string> result;
            try
            {
                result = await job.RunAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await runs.FinishAsync(runId, succeeded: false, $"AUX-{EventCodes.Host.UnhandledException}", exception.GetType().Name, CancellationToken.None);
                throw;
            }

            await runs.FinishAsync(runId, result.IsSuccess, result.Error?.DisplayCode, result.IsSuccess ? result.Value : result.Error!.Description, cancellationToken);
            return result;
        }, cancellationToken);
}
