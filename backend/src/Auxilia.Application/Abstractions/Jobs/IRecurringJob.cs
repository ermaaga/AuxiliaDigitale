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

/// <summary>Records runs in <c>ops.job_runs</c> of the current tenant, outside the job's own transactions.</summary>
public interface IJobRunStore
{
    Task<Guid> StartAsync(string jobCode, CancellationToken cancellationToken);

    Task FinishAsync(Guid runId, bool succeeded, string? errorCode, string? summary, CancellationToken cancellationToken);
}
