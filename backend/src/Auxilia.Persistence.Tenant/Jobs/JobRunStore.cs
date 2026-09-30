using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Jobs;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Persistence.Tenant.Operations;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Jobs;

/// <inheritdoc cref="IJobRunStore"/>
internal sealed class JobRunStore : IJobRunStore
{
    private readonly TenantDbContextFactory databases;
    private readonly ICurrentUser currentUser;
    private readonly TimeProvider timeProvider;

    public JobRunStore(TenantDbContextFactory databases, ICurrentUser currentUser, TimeProvider timeProvider)
    {
        this.databases = databases;
        this.currentUser = currentUser;
        this.timeProvider = timeProvider;
    }

    public async Task<Guid> StartAsync(string jobCode, CancellationToken cancellationToken)
    {
        var run = new JobRun
        {
            Id = Guid.CreateVersion7(),
            JobCode = jobCode,
            Status = JobRunStatus.Running,
            StartedAt = timeProvider.GetUtcNow(),
            ActorType = currentUser.ActorType.ToString(),
            ActorId = currentUser.UserId,
        };

        await using var db = await databases.CreateOutsideOperationAsync(cancellationToken);
        db.Set<JobRun>().Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return run.Id;
    }

    public async Task FinishAsync(Guid runId, bool succeeded, string? errorCode, string? summary, CancellationToken cancellationToken)
    {
        await using var db = await databases.CreateOutsideOperationAsync(cancellationToken);
        var run = await db.Set<JobRun>().SingleAsync(item => item.Id == runId, cancellationToken);
        run.Status = succeeded ? JobRunStatus.Succeeded : JobRunStatus.Failed;
        run.FinishedAt = timeProvider.GetUtcNow();
        run.ErrorCode = errorCode;
        run.Summary = summary is { Length: > 2000 } ? summary[..2000] : summary;
        await db.SaveChangesAsync(cancellationToken);
    }
}
