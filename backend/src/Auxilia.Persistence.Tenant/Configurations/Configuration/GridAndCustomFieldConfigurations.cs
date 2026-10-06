using System.Text.Json;

using Auxilia.Domain.Configuration;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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

internal sealed class GridViewConfiguration : IEntityTypeConfiguration<GridView>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<GridView> builder)
    {
        builder.ToTable("user_grid_views", TenantSchemas.Configuration);
        builder.HasKey(view => view.Id);
        builder.Property(view => view.Id).ValueGeneratedNever();
        builder.Property(view => view.GridKey).HasMaxLength(GridLayout.GridKeyMaxLength);
        builder.Property(view => view.Name).HasMaxLength(GridView.NameMaxLength).HasColumnType("citext");
        builder.Property(view => view.HiddenColumns).HasColumnType("text[]");
        builder.Property(view => view.Filters)
            .HasColumnType("jsonb")
            .HasConversion(
                filters => JsonSerializer.Serialize(filters, JsonOptions),
                json => JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? new Dictionary<string, string>(StringComparer.Ordinal),
                new ValueComparer<Dictionary<string, string>>(
                    (left, right) => left!.Count == right!.Count && !left.Except(right).Any(),
                    filters => filters.Aggregate(0, (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value)),
                    filters => new Dictionary<string, string>(filters, StringComparer.Ordinal)));
        builder.Property(view => view.Sort).HasMaxLength(GridView.SortMaxLength);
        builder.HasOne<Domain.Identity.User>().WithMany().HasForeignKey(view => view.UserId).OnDelete(DeleteBehavior.Cascade);

        // One name per user and grid (whatever the case), at most one default.
        builder.HasIndex(view => new { view.UserId, view.GridKey, view.Name }).IsUnique();
        builder.HasIndex(view => new { view.UserId, view.GridKey }).IsUnique().HasFilter("is_default");
    }
}
