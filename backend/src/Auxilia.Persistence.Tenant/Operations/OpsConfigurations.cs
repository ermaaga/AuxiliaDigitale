using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Operations;

internal sealed class DataMigrationHistoryConfiguration : IEntityTypeConfiguration<DataMigrationHistoryEntry>
{
    public void Configure(EntityTypeBuilder<DataMigrationHistoryEntry> builder)
    {
        builder.ToTable("data_migrations_history", TenantSchemas.Ops);
        builder.HasKey(entry => entry.Key);
        builder.Property(entry => entry.Key).HasMaxLength(50);
        builder.Property(entry => entry.Description).HasMaxLength(500);
    }
}

internal sealed class JobRunConfiguration : IEntityTypeConfiguration<JobRun>
{
    public void Configure(EntityTypeBuilder<JobRun> builder)
    {
        builder.ToTable("job_runs", TenantSchemas.Ops, table =>
            table.HasCheckConstraint("ck_job_runs_status", "status IN ('Running', 'Succeeded', 'Failed')"));
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).ValueGeneratedNever();
        builder.Property(run => run.JobCode).HasMaxLength(100);
        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(run => run.ActorType).HasMaxLength(20);
        builder.Property(run => run.ErrorCode).HasMaxLength(20);
        builder.Property(run => run.Summary).HasMaxLength(2000);
        builder.HasIndex(run => new { run.JobCode, run.StartedAt });
    }
}

internal sealed class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> builder)
    {
        builder.ToTable("number_sequences", TenantSchemas.Ops);
        builder.HasKey(sequence => new { sequence.Scope, sequence.Year });
        builder.Property(sequence => sequence.Scope).HasMaxLength(50);
    }
}

internal sealed class LegacyIdMappingConfiguration : IEntityTypeConfiguration<LegacyIdMapping>
{
    public void Configure(EntityTypeBuilder<LegacyIdMapping> builder)
    {
        builder.ToTable("legacy_id_map", TenantSchemas.Ops);
        builder.HasKey(mapping => new { mapping.Entity, mapping.LegacyId });
        builder.Property(mapping => mapping.Entity).HasMaxLength(100);
        builder.HasIndex(mapping => mapping.NewId);
    }
}

internal sealed class EntityChangeConfiguration : IEntityTypeConfiguration<EntityChange>
{
    public void Configure(EntityTypeBuilder<EntityChange> builder)
    {
        builder.ToTable("entity_changes", TenantSchemas.Audit, table =>
        {
            table.HasCheckConstraint("ck_entity_changes_action", "action IN ('Created', 'Updated', 'Deleted')");
            table.HasCheckConstraint("ck_entity_changes_actor_type", "actor_type IN ('Anonymous', 'User', 'Platform', 'System')");
        });
        builder.HasKey(change => change.Id);
        builder.Property(change => change.Id).UseIdentityAlwaysColumn();
        builder.Property(change => change.EntityType).HasMaxLength(100);
        builder.Property(change => change.Action).HasMaxLength(20);
        builder.Property(change => change.Changes).HasColumnType("jsonb");
        builder.Property(change => change.ActorType).HasMaxLength(20);
        builder.Property(change => change.TraceId).HasMaxLength(64);
        builder.HasIndex(change => new { change.EntityType, change.EntityId, change.OccurredAt });
    }
}
