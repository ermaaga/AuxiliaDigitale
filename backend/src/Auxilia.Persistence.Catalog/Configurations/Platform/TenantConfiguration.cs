using Auxilia.Domain.Platform;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Catalog.Configurations.Platform;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(tenant => tenant.Id);
        builder.Property(tenant => tenant.Id).ValueGeneratedNever();
        builder.Property(tenant => tenant.Slug).HasColumnType("citext").HasMaxLength(SharedKernel.Tenancy.TenantSlug.MaxLength);
        builder.HasIndex(tenant => tenant.Slug).IsUnique();
        builder.Property(tenant => tenant.DisplayName).HasMaxLength(Tenant.DisplayNameMaxLength);
        builder.Property(tenant => tenant.Status).HasConversion<string>().HasMaxLength(CatalogConventions.EnumMaxLength);
        builder.HasIndex(tenant => tenant.Status);
        builder.Property(tenant => tenant.ConnectionSecret);
        builder.Property(tenant => tenant.SchemaVersion).HasMaxLength(Tenant.VersionMaxLength);
        builder.Property(tenant => tenant.DataVersion).HasMaxLength(Tenant.VersionMaxLength);
        builder.Property(tenant => tenant.DefaultLanguage).HasMaxLength(Tenant.LanguageMaxLength);
        builder.Property(tenant => tenant.TimeZone).HasMaxLength(Tenant.TimeZoneMaxLength);
        builder.Ignore(tenant => tenant.DomainEvents);
        builder.HasAuditColumns();
        builder.HasXminVersion();
    }
}
