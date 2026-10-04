using Auxilia.Domain.Cases;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Cases;

internal sealed class ServiceChecklistItemConfiguration : IEntityTypeConfiguration<ServiceChecklistItem>
{
    public void Configure(EntityTypeBuilder<ServiceChecklistItem> builder)
    {
        builder.ToTable("service_checklist_items", TenantSchemas.Cases);
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.Name).HasMaxLength(ServiceChecklistItem.NameMaxLength);
        builder.HasOne<Service>().WithMany().HasForeignKey(item => item.ServiceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ServiceFolder>().WithMany().HasForeignKey(item => item.FolderId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(item => new { item.ServiceId, item.Order });
        builder.HasIndex(item => item.FolderId);
    }
}

internal sealed class CaseChecklistMarkConfiguration : IEntityTypeConfiguration<CaseChecklistMark>
{
    public void Configure(EntityTypeBuilder<CaseChecklistMark> builder)
    {
        builder.ToTable("case_checklist_marks", TenantSchemas.Cases);
        builder.HasKey(mark => new { mark.CaseId, mark.ItemId });
        builder.HasOne<Case>().WithMany().HasForeignKey(mark => mark.CaseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ServiceChecklistItem>().WithMany().HasForeignKey(mark => mark.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Domain.Identity.User>().WithMany().HasForeignKey(mark => mark.CheckedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(mark => mark.ItemId);
        builder.HasIndex(mark => mark.CheckedByUserId);
    }
}
