using Auxilia.Domain.Platform;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Catalog.Configurations.Platform;

internal sealed class PlatformModuleConfiguration : IEntityTypeConfiguration<PlatformModule>
{
    public void Configure(EntityTypeBuilder<PlatformModule> builder)
    {
        builder.ToTable("modules");
        builder.HasKey(module => module.Id);
        builder.Property(module => module.Id).HasColumnName("code").HasMaxLength(PlatformModule.CodeMaxLength).ValueGeneratedNever();
        builder.Property(module => module.Kind).HasConversion<string>().HasMaxLength(CatalogConventions.EnumMaxLength);
        builder.Property(module => module.NameKey).HasMaxLength(PlatformModule.NameKeyMaxLength);
        builder.HasAuditColumns();
    }
}
