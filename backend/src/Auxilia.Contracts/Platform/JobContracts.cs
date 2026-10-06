namespace Auxilia.Contracts.Platform;

/// <summary>A recurring job of the tenant (D-15: never scheduled, run by hand) with its last run, if any.</summary>
public sealed record JobResponse(string Code, string SuggestedFrequency, bool IsRunning, JobRunResponse? LastRun);

/// <summary>
/// One run of a job (<c>ops.job_runs</c>). <paramref name="Status"/> is <c>Running</c>, <c>Succeeded</c> or
/// <c>Failed</c>; <paramref name="ActorType"/> is <c>Platform</c> (console), <c>System</c> (auxctl) or <c>User</c>,
/// with the name of the platform user when known.
/// </summary>
public sealed record JobRunResponse(
    Guid Id,
    string JobCode,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string ActorType,
    string? ActorName,
    string? ErrorCode,
    string? Summary);
