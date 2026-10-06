using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Jobs;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Contracts.Messages.V1.Platform;
using Auxilia.Contracts.Platform;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Jobs;

/// <summary>The recurring jobs of the current tenant for the console page (N02, D-15): last run and history.</summary>
public interface IJobQueryService
{
    Task<IReadOnlyList<JobResponse>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The latest runs of every job, newest first (at most <see cref="MaxRuns"/>).</summary>
    Task<IReadOnlyList<JobRunResponse>> RunsAsync(int take, CancellationToken cancellationToken);

    const int MaxRuns = 100;
}

/// <summary>"Run now" from the console: the Worker runs the job (<see cref="RunRecurringJobCommand"/>).</summary>
public interface IJobManager
{
    Task<Result> RequestRunAsync(string jobCode, CancellationToken cancellationToken);
}

internal sealed class JobQueryService(IJobRunner jobs, IJobRunStore runs, IPlatformIdentityStore platformUsers) : IJobQueryService
{
    public async Task<IReadOnlyList<JobResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var names = await PlatformNamesAsync(cancellationToken);
        var result = new List<JobResponse>(jobs.Jobs.Count);
        foreach (var job in jobs.Jobs)
        {
            // One query per job: a tenant has a handful of jobs, and each needs its own latest run.
            var latest = await runs.RecentAsync(job.Code, 1, cancellationToken);
            var last = latest.Count > 0 ? latest[0] : null;
            result.Add(new JobResponse(job.Code, job.SuggestedFrequency, last?.Status == "Running", last is null ? null : Map(last, names)));
        }

        return result;
    }

    public async Task<IReadOnlyList<JobRunResponse>> RunsAsync(int take, CancellationToken cancellationToken)
    {
        var names = await PlatformNamesAsync(cancellationToken);
        var rows = await runs.RecentAsync(null, Math.Clamp(take, 1, IJobQueryService.MaxRuns), cancellationToken);
        return rows.Select(row => Map(row, names)).ToList();
    }

    private async Task<IReadOnlyDictionary<Guid, string>> PlatformNamesAsync(CancellationToken cancellationToken) =>
        (await platformUsers.ListUsersAsync(cancellationToken)).ToDictionary(user => user.Id, user => user.DisplayName);

    private static JobRunResponse Map(JobRunRow row, IReadOnlyDictionary<Guid, string> platformNames) =>
        new(
            row.Id,
            row.JobCode,
            row.Status,
            row.StartedAt,
            row.FinishedAt,
            row.ActorType,
            row.ActorType == "Platform" && row.ActorId is { } id ? platformNames.GetValueOrDefault(id) : null,
            row.ErrorCode,
            row.Summary);
}

internal sealed class JobManager(IOperationRunner operations, IJobRunner jobs, IMessageOutbox outbox) : IJobManager
{
    public Task<Result> RequestRunAsync(string jobCode, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Jobs.RequestRun, new { JobCode = jobCode }, async scope =>
        {
            if (!jobs.Jobs.Any(job => string.Equals(job.Code, jobCode, StringComparison.Ordinal)))
            {
                return Result.Failure(Errors.Jobs.JobNotFound(jobCode));
            }

            // The Worker takes the job lock: a run already in progress makes this request a no-op.
            await outbox.EnqueueAsync(scope, new RunRecurringJobCommand(jobCode), cancellationToken);
            return Result.Success();
        }, cancellationToken);
}
