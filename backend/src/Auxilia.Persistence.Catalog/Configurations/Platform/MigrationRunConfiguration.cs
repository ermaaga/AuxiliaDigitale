using Auxilia.Domain.Platform;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Catalog.Configurations.Platform;

internal sealed class MigrationRunConfiguration : IEntityTypeConfiguration<MigrationRun>
{
    public void Configure(EntityTypeBuilder<MigrationRun> builder)
    {
        builder.ToTable("migration_runs");
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).ValueGeneratedNever();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(run => run.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(run => new { run.TenantId, run.StartedAt });
        builder.Property(run => run.Kind).HasConversion<string>().HasMaxLength(CatalogConventions.EnumMaxLength);
        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(CatalogConventions.EnumMaxLength);
        builder.Property(run => run.Target).HasMaxLength(MigrationRun.TargetMaxLength);
        builder.Property(run => run.Actor).HasMaxLength(MigrationRun.ActorMaxLength);
        builder.Property(run => run.ErrorCode).HasMaxLength(MigrationRun.ErrorCodeMaxLength);
        builder.Property(run => run.Message).HasMaxLength(MigrationRun.MessageMaxLength);
    }
}
