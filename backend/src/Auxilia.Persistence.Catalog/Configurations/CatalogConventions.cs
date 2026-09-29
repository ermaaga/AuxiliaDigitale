using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Catalog.Configurations;

/// <summary>Columns shared by catalog tables (skill auxilia-ef-migration).</summary>
internal static class CatalogConventions
{
    public const string CreatedAt = "CreatedAt";
    public const string CreatedBy = "CreatedBy";
    public const string UpdatedAt = "UpdatedAt";
    public const string UpdatedBy = "UpdatedBy";
    public const string Version = "Version";
    public const int ActorMaxLength = 200;
    public const int EnumMaxLength = 50;

    /// <summary><c>created_at/created_by/updated_at/updated_by</c> as shadow properties, filled by <c>CatalogAuditInterceptor</c>.</summary>
    public static void HasAuditColumns<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        builder.Property<DateTimeOffset>(CreatedAt);
        builder.Property<string>(CreatedBy).HasMaxLength(ActorMaxLength);
        builder.Property<DateTimeOffset?>(UpdatedAt);
        builder.Property<string?>(UpdatedBy).HasMaxLength(ActorMaxLength);
    }

    /// <summary>PostgreSQL <c>xmin</c> as optimistic concurrency token (also the source of ETags).</summary>
    public static void HasXminVersion<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class =>
        builder.Property<uint>(Version).IsRowVersion();
}
