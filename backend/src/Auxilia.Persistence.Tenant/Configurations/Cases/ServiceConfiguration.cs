using Auxilia.Domain.Cases;
using Auxilia.Domain.Directory;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Cases;

internal sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("services", TenantSchemas.Cases, table =>
        {
            table.HasCheckConstraint("ck_services_price", "price >= 0");
            table.HasCheckConstraint("ck_services_duration_days", $"duration_days BETWEEN 1 AND {Service.MaxDurationDays}");
        });
        builder.HasKey(service => service.Id);
        builder.Property(service => service.Id).ValueGeneratedNever();
        builder.Property(service => service.Name).HasMaxLength(Service.NameMaxLength).HasColumnType("citext");
        builder.Property(service => service.Description).HasMaxLength(Service.DescriptionMaxLength);
        builder.Property(service => service.Price).HasPrecision(12, 2);
        builder.Property(service => service.Currency).HasMaxLength(3).IsFixedLength();
        builder.HasOne<ServiceCategory>().WithMany().HasForeignKey(service => service.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Specialization>().WithMany().HasForeignKey(service => service.SpecializationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(service => service.CategoryId);
        builder.HasIndex(service => service.SpecializationId);

        // One service per name among the ones not deleted.
        builder.HasIndex(service => service.Name).IsUnique().HasFilter("NOT is_deleted");
    }
}
