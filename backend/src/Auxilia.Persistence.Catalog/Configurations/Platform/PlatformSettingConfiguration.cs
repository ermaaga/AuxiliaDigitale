using Auxilia.Domain.Platform;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Catalog.Configurations.Platform;

internal sealed class PlatformSettingConfiguration : IEntityTypeConfiguration<PlatformSetting>
{
    public void Configure(EntityTypeBuilder<PlatformSetting> builder)
    {
        builder.ToTable("platform_settings");
        builder.HasKey(setting => setting.Key);
        builder.Property(setting => setting.Key).HasMaxLength(PlatformSetting.KeyMaxLength);
        builder.Property(setting => setting.JsonValue).HasColumnName("value").HasColumnType("jsonb");
        builder.HasAuditColumns();
        builder.HasXminVersion();
    }
}
