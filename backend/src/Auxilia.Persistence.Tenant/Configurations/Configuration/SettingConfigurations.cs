using Auxilia.Domain.Configuration;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Configuration;

internal sealed class TenantSettingConfiguration : IEntityTypeConfiguration<TenantSetting>
{
    public void Configure(EntityTypeBuilder<TenantSetting> builder)
    {
        builder.ToTable("settings", TenantSchemas.Configuration);
        builder.HasKey(setting => setting.Id);
        builder.Property(setting => setting.Id).ValueGeneratedNever();
        builder.Property(setting => setting.Key).HasMaxLength(TenantSetting.KeyMaxLength);
        builder.Property(setting => setting.JsonValue).HasColumnName("value").HasColumnType("jsonb");
        builder.HasIndex(setting => setting.Key).IsUnique();
    }
}

internal sealed class UserSettingConfiguration : IEntityTypeConfiguration<UserSetting>
{
    public void Configure(EntityTypeBuilder<UserSetting> builder)
    {
        // The foreign key to identity.users is added with the identity schema (P2-01).
        builder.ToTable("user_settings", TenantSchemas.Configuration);
        builder.HasKey(setting => setting.Id);
        builder.Property(setting => setting.Id).ValueGeneratedNever();
        builder.Property(setting => setting.Key).HasMaxLength(TenantSetting.KeyMaxLength);
        builder.Property(setting => setting.JsonValue).HasColumnName("value").HasColumnType("jsonb");
        builder.HasIndex(setting => new { setting.UserId, setting.Key }).IsUnique();
    }
}
