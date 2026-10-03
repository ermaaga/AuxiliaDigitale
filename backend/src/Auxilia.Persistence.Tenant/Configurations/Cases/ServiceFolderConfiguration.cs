using Auxilia.Domain.Cases;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Cases;

internal sealed class ServiceFolderConfiguration : IEntityTypeConfiguration<ServiceFolder>
{
    public void Configure(EntityTypeBuilder<ServiceFolder> builder)
    {
        builder.ToTable("service_folders", TenantSchemas.Cases);
        builder.HasKey(folder => folder.Id);
        builder.Property(folder => folder.Id).ValueGeneratedNever();
        builder.Property(folder => folder.Name).HasMaxLength(ServiceFolder.NameMaxLength);
        builder.HasOne<Service>().WithMany().HasForeignKey(folder => folder.ServiceId).OnDelete(DeleteBehavior.Restrict);

        // Deleting a folder deletes its subfolders (the manager removes the whole subtree; the database agrees).
        builder.HasOne<ServiceFolder>().WithMany().HasForeignKey(folder => folder.ParentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(folder => new { folder.ServiceId, folder.ParentId, folder.SortOrder });
        builder.HasIndex(folder => folder.ParentId);
    }
}
