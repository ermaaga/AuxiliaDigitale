using Auxilia.Domain.Cases;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Documents;
using Auxilia.Domain.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Documents;

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents", TenantSchemas.Documents, table =>
        {
            table.HasCheckConstraint("ck_documents_status", "status IN ('Processing', 'Available', 'Damaged')");
            table.HasCheckConstraint("ck_documents_folder_needs_case", "folder_id IS NULL OR case_id IS NOT NULL");
        });
        builder.HasKey(document => document.Id);
        builder.Property(document => document.Id).ValueGeneratedNever();
        builder.Property(document => document.FileName).HasMaxLength(Document.FileNameMaxLength);
        builder.Property(document => document.StorageKey).HasMaxLength(Document.StorageKeyMaxLength);
        builder.HasIndex(document => document.StorageKey).IsUnique();
        builder.Property(document => document.ContentType).HasMaxLength(Document.ContentTypeMaxLength);
        builder.Property(document => document.Sha256).HasMaxLength(64).IsFixedLength();
        builder.Property(document => document.Description).HasMaxLength(Document.DescriptionMaxLength);
        builder.Property(document => document.CustomFields).HasColumnType("jsonb");
        builder.Property(document => document.Status).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(document => document.Extension);

        builder.HasOne<ClientProfile>().WithMany().HasForeignKey(document => document.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Case>().WithMany().HasForeignKey(document => document.CaseId).OnDelete(DeleteBehavior.SetNull);

        // F33: removing a folder of the template leaves its documents without a folder.
        builder.HasOne<ServiceFolder>().WithMany().HasForeignKey(document => document.FolderId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<DocumentArea>().WithMany().HasForeignKey(document => document.AreaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(document => document.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(document => new { document.ClientId, document.UploadedAt });
        builder.HasIndex(document => new { document.CaseId, document.FolderId });
        builder.HasIndex(document => document.FolderId);
        builder.HasIndex(document => document.AreaId);
        builder.HasIndex(document => document.UploadedByUserId);

        // Default order of the lists (newest first): the page stops after its rows instead of sorting every visible document (H-02).
        builder.HasIndex(document => new { document.UploadedAt, document.Id });
    }
}

internal sealed class DocumentAreaConfiguration : IEntityTypeConfiguration<DocumentArea>
{
    public void Configure(EntityTypeBuilder<DocumentArea> builder)
    {
        builder.ToTable("document_areas", TenantSchemas.Documents);
        builder.HasKey(area => area.Id);
        builder.Property(area => area.Id).ValueGeneratedNever();
        builder.Property(area => area.Name).HasMaxLength(DocumentArea.NameMaxLength).HasColumnType("citext");
        builder.HasIndex(area => area.Name).IsUnique().HasFilter("is_active");
    }
}
