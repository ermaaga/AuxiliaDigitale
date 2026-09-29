using Auxilia.SharedKernel.Domain;

namespace Auxilia.Domain.Platform;

public enum MigrationRunKind
{
    CatalogSchema,
    TenantSchema,
    TenantData,
    Provisioning,
    LegacyImport,

    /// <summary>Manual run of a recurring job (D-15).</summary>
    Job,
}

public enum MigrationRunStatus
{
    Running,
    Succeeded,
    Failed,
}

/// <summary>
/// One run of a migration, provisioning, legacy import or manual job (catalog <c>migration_runs</c>), written by
/// <c>auxctl</c> and the platform API. Details go to the logs; this row is the summary shown in the console.
/// </summary>
public sealed class MigrationRun : Entity<Guid>
{
    public const int TargetMaxLength = 200;
    public const int ActorMaxLength = 200;
    public const int ErrorCodeMaxLength = 20;
    public const int MessageMaxLength = 2000;

    public MigrationRun(Guid id, Guid? tenantId, MigrationRunKind kind, string target, string actor, DateTimeOffset startedAt)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        TenantId = tenantId;
        Kind = kind;
        Target = target;
        Actor = actor;
        StartedAt = startedAt;
        Status = MigrationRunStatus.Running;
    }

    private MigrationRun()
    {
        Target = Actor = string.Empty;
    }

    /// <summary>Null for runs on the catalog itself.</summary>
    public Guid? TenantId { get; private set; }

    public MigrationRunKind Kind { get; private set; }

    /// <summary>What was run, e.g. the target migration or job code.</summary>
    public string Target { get; private set; }

    /// <summary>Who started it, e.g. <c>platform:{userId}</c> or <c>auxctl</c>.</summary>
    public string Actor { get; private set; }

    public MigrationRunStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? Message { get; private set; }

    public void Succeed(DateTimeOffset finishedAt, string? message = null) => Finish(MigrationRunStatus.Succeeded, finishedAt, null, message);

    public void Fail(DateTimeOffset finishedAt, string errorCode, string? message = null) => Finish(MigrationRunStatus.Failed, finishedAt, errorCode, message);

    private void Finish(MigrationRunStatus status, DateTimeOffset finishedAt, string? errorCode, string? message)
    {
        if (Status != MigrationRunStatus.Running)
        {
            throw new InvalidOperationException("The run is already finished.");
        }

        Status = status;
        FinishedAt = finishedAt;
        ErrorCode = errorCode;
        Message = message is { Length: > MessageMaxLength } ? message[..MessageMaxLength] : message;
    }
}
