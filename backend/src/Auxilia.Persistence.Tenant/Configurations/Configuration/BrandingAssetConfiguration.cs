using Auxilia.Domain.Configuration;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Configuration;

internal sealed class BrandingAssetConfiguration : IEntityTypeConfiguration<BrandingAsset>
{
    public void Configure(EntityTypeBuilder<BrandingAsset> builder)
    {
        builder.ToTable("branding_assets", TenantSchemas.Configuration);
        builder.HasKey(asset => asset.Id);
        builder.Property(asset => asset.Id).ValueGeneratedNever();
        builder.Property(asset => asset.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(asset => asset.ContentType).HasMaxLength(BrandingAsset.ContentTypeMaxLength);
        builder.Property(asset => asset.Hash).HasMaxLength(BrandingAsset.HashLength).IsFixedLength();
        builder.HasIndex(asset => asset.Kind).IsUnique();
    }
}
