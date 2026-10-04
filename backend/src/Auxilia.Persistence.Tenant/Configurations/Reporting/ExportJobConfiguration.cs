using Auxilia.Domain.Identity;
using Auxilia.Domain.Reporting;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Reporting;

internal sealed class ExportJobConfiguration : IEntityTypeConfiguration<ExportJob>
{
    public void Configure(EntityTypeBuilder<ExportJob> builder)
    {
        builder.ToTable("exports", TenantSchemas.Reporting, table =>
            table.HasCheckConstraint("ck_exports_status", "status IN ('Queued', 'Ready', 'Failed')"));
        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id).ValueGeneratedNever();
        builder.Property(job => job.SourceKey).HasMaxLength(50);
        builder.Property(job => job.Format).HasMaxLength(10);
        builder.Property(job => job.Request).HasColumnType("jsonb");
        builder.Property(job => job.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(job => job.FileName).HasMaxLength(ExportJob.FileNameMaxLength);
        builder.Property(job => job.ContentType).HasMaxLength(100);
        builder.Property(job => job.ErrorCode).HasMaxLength(20);
        builder.HasOne<User>().WithMany().HasForeignKey(job => job.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(job => new { job.UserId, job.CreatedAt });
        builder.HasIndex(job => job.ExpiresAt);
    }
}
