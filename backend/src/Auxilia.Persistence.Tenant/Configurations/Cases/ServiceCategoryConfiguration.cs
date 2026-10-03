using Auxilia.Domain.Cases;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Cases;

internal sealed class ServiceCategoryConfiguration : IEntityTypeConfiguration<ServiceCategory>
{
    public void Configure(EntityTypeBuilder<ServiceCategory> builder)
    {
        builder.ToTable("service_categories", TenantSchemas.Cases);
        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id).ValueGeneratedNever();
        builder.Property(category => category.Name).HasMaxLength(ServiceCategory.NameMaxLength).HasColumnType("citext");
        builder.Property(category => category.Description).HasMaxLength(ServiceCategory.DescriptionMaxLength);

        // One active category per name (inactive ones keep theirs).
        builder.HasIndex(category => category.Name).IsUnique().HasFilter("is_active");
    }
}
