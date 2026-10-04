using Auxilia.Domain.Imports;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Imports;

internal sealed class ImportTypeConfiguration : IEntityTypeConfiguration<ImportType>
{
    public void Configure(EntityTypeBuilder<ImportType> builder)
    {
        builder.ToTable("import_types", TenantSchemas.Imports);
        builder.HasKey(type => type.Id);
        builder.Property(type => type.Id).ValueGeneratedNever();
        builder.Property(type => type.Name).HasMaxLength(ImportType.NameMaxLength);
        builder.Property(type => type.TargetEntity).HasMaxLength(ImportType.EntityMaxLength);
        builder.HasIndex(type => type.Name).IsUnique();
    }
}

internal sealed class ImportJobConfiguration : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(EntityTypeBuilder<ImportJob> builder)
    {
        builder.ToTable("import_jobs", TenantSchemas.Imports, table => table.HasCheckConstraint(
            "ck_import_jobs_status", "status IN ('Pending', 'Validating', 'AwaitingConfirmation', 'Processing', 'Completed', 'Failed', 'Cancelled')"));
        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id).ValueGeneratedNever();
        builder.Property(job => job.Name).HasMaxLength(ImportJob.NameMaxLength);
        builder.Property(job => job.FileName).HasMaxLength(ImportJob.FileNameMaxLength);
        builder.Property(job => job.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(job => job.ErrorCode).HasMaxLength(20);
        builder.Property(job => job.ErrorMessage).HasMaxLength(500);
        builder.Ignore(job => job.IsFinished);
        builder.HasOne<ImportType>().WithMany().HasForeignKey(job => job.ImportTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(job => job.CreatedAt);
    }
}

internal sealed class ImportJobRowConfiguration : IEntityTypeConfiguration<ImportJobRow>
{
    public void Configure(EntityTypeBuilder<ImportJobRow> builder)
    {
        builder.ToTable("import_job_rows", TenantSchemas.Imports, table =>
            table.HasCheckConstraint("ck_import_job_rows_status", "status IN ('Valid', 'Invalid', 'Imported', 'Failed')"));
        builder.HasKey(row => new { row.JobId, row.RowNumber });
        builder.Property(row => row.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(row => row.Data).HasColumnType("jsonb");
        builder.Property(row => row.Errors).HasColumnType("jsonb");
        builder.HasOne<ImportJob>().WithMany().HasForeignKey(row => row.JobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(row => new { row.JobId, row.Status });
    }
}
