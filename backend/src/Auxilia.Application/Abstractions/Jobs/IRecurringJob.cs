using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Abstractions.Jobs;

/// <summary>
/// Periodic logic of a module (D-15): registered, never scheduled; run by hand for one tenant (console or
/// <c>auxctl jobs run</c>) in a scope where the tenant is already set. Returns a short summary for <c>ops.job_runs</c>.
/// </summary>
public interface IRecurringJob
{
    /// <summary>Stable code, e.g. <c>cases.expiry</c>.</summary>
    string Code { get; }

    string Description { get; }

    /// <summary>Frequency a future scheduler would use, e.g. <c>daily</c> (informational).</summary>
    string SuggestedFrequency { get; }

    Task<Result<string>> RunAsync(CancellationToken cancellationToken);
}

/// <summary>A run of <c>ops.job_runs</c>: status <c>Running</c>, <c>Succeeded</c> or <c>Failed</c>.</summary>
public sealed record JobRunRow(
    Guid Id,
    string JobCode,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string ActorType,
    Guid? ActorId,
    string? ErrorCode,
    string? Summary);

/// <summary>Records runs in <c>ops.job_runs</c> of the current tenant, outside the job's own transactions, and reads them back.</summary>
public interface IJobRunStore
{
    /// <summary>The latest runs, newest first; of one job when <paramref name="jobCode"/> is given.</summary>
    Task<IReadOnlyList<JobRunRow>> RecentAsync(string? jobCode, int take, CancellationToken cancellationToken);

    Task<Guid> StartAsync(string jobCode, CancellationToken cancellationToken);

    Task FinishAsync(Guid runId, bool succeeded, string? errorCode, string? summary, CancellationToken cancellationToken);
}

/// <summary>
/// Lock of a job run in the current tenant (skill auxilia-messaging-rebus): a second run of the same job while one is
/// running is refused. Held until disposed.
/// </summary>
public interface IJobLock
{
    /// <returns>The held lock, or <c>null</c> when another run holds it.</returns>
    Task<IAsyncDisposable?> TryAcquireAsync(string jobCode, CancellationToken cancellationToken);
}
