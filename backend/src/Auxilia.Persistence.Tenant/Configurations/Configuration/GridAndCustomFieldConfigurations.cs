using Auxilia.Domain.Configuration;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Configuration;

internal sealed class CustomFieldDefinitionConfiguration : IEntityTypeConfiguration<CustomFieldDefinition>
{
    public void Configure(EntityTypeBuilder<CustomFieldDefinition> builder)
    {
        builder.ToTable("custom_field_definitions", TenantSchemas.Configuration);
        builder.HasKey(definition => definition.Id);
        builder.Property(definition => definition.Id).ValueGeneratedNever();
        builder.Property(definition => definition.EntityType).HasMaxLength(CustomFieldDefinition.EntityTypeMaxLength);
        builder.Property(definition => definition.Key).HasMaxLength(CustomFieldDefinition.KeyMaxLength).HasColumnType("citext");
        builder.Property(definition => definition.Label).HasMaxLength(CustomFieldDefinition.LabelMaxLength);
        builder.Property(definition => definition.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(definition => definition.Options).HasColumnType("text[]");
        builder.Property(definition => definition.GroupName).HasMaxLength(CustomFieldDefinition.GroupNameMaxLength);
        builder.Property(definition => definition.BadgeColor).HasMaxLength(7);

        // The key names the value inside custom_fields: unique per entity, whatever the case.
        builder.HasIndex(definition => new { definition.EntityType, definition.Key }).IsUnique();
    }
}

internal sealed class GridLayoutConfiguration : IEntityTypeConfiguration<GridLayout>
{
    public void Configure(EntityTypeBuilder<GridLayout> builder)
    {
        builder.ToTable("grid_layouts", TenantSchemas.Configuration);
        builder.HasKey(layout => layout.Id);
        builder.Property(layout => layout.Id).ValueGeneratedNever();
        builder.Property(layout => layout.GridKey).HasMaxLength(GridLayout.GridKeyMaxLength);
        builder.Property(layout => layout.Role).HasMaxLength(GridLayout.RoleMaxLength);
        builder.OwnsMany(layout => layout.Columns, columns => columns.ToJson("columns"));
        builder.HasIndex(layout => new { layout.GridKey, layout.Role }).IsUnique();
    }
}
