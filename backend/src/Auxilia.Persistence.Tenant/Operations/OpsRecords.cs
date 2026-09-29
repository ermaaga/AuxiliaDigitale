namespace Auxilia.Persistence.Tenant.Operations;

/// <summary>A data-migration applied to this tenant (<c>ops.data_migrations_history</c>).</summary>
public sealed class DataMigrationHistoryEntry
{
    public required string Key { get; init; }

    public required string Description { get; init; }

    public DateTimeOffset AppliedAt { get; init; }

    public long DurationMs { get; init; }

    /// <summary>True when marked as applied by the initial seed of a new tenant instead of being run.</summary>
    public bool CoveredBySeed { get; init; }
}

public enum JobRunStatus
{
    Running,
    Succeeded,
    Failed,
}

/// <summary>A manual run of a recurring job in this tenant (<c>ops.job_runs</c>, D-15), shown in the System console.</summary>
public sealed class JobRun
{
    public Guid Id { get; init; }

    public required string JobCode { get; init; }

    public JobRunStatus Status { get; set; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary><c>User</c>, <c>Platform</c> or <c>System</c>.</summary>
    public required string ActorType { get; init; }

    public Guid? ActorId { get; init; }

    public string? ErrorCode { get; set; }

    public string? Summary { get; set; }
}

/// <summary>Last number issued per scope and year (<c>ops.number_sequences</c>): case, request and client numbers.</summary>
public sealed class NumberSequence
{
    public required string Scope { get; init; }

    public int Year { get; init; }

    public long LastValue { get; set; }
}

/// <summary>Legacy <c>int</c> id → new Guid, written by the legacy import (<c>ops.legacy_id_map</c>).</summary>
public sealed class LegacyIdMapping
{
    public required string Entity { get; init; }

    public int LegacyId { get; init; }

    public Guid NewId { get; init; }

    public DateTimeOffset ImportedAt { get; init; }
}

/// <summary>One recorded change of an <c>IAuditable</c> entity (<c>audit.entity_changes</c>), shown as the record history.</summary>
public sealed class EntityChange
{
    public long Id { get; init; }

    public required string EntityType { get; init; }

    public Guid EntityId { get; init; }

    /// <summary><c>Created</c>, <c>Updated</c> or <c>Deleted</c>.</summary>
    public required string Action { get; init; }

    /// <summary>JSON object: property → value (created) or <c>{ "old": …, "new": … }</c> (updated).</summary>
    public required string Changes { get; init; }

    public required string ActorType { get; init; }

    public Guid? ActorId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public string? TraceId { get; init; }
}
