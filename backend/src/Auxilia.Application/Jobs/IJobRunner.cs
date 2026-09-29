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

    public JobRunner(IEnumerable<IRecurringJob> jobs, IOperationRunner operations, IJobRunStore runs)
    {
        Jobs = jobs.OrderBy(job => job.Code, StringComparer.Ordinal).ToArray();
        this.operations = operations;
        this.runs = runs;
    }

    public IReadOnlyList<IRecurringJob> Jobs { get; }

    // Not a write operation: the run row must survive a failed job; the job's own changes go through Managers.
    // A distributed lock per tenant and job arrives with Redis (P1-10).
    public Task<Result<string>> RunAsync(string jobCode, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Jobs.RunJob, new { JobCode = jobCode }, async scope =>
        {
            var job = Jobs.FirstOrDefault(item => string.Equals(item.Code, jobCode, StringComparison.Ordinal));
            if (job is null)
            {
                return Errors.Jobs.JobNotFound(jobCode);
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
