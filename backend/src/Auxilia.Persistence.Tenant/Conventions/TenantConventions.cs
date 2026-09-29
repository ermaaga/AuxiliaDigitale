using System.Linq.Expressions;

using Auxilia.SharedKernel.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Auxilia.Persistence.Tenant.Conventions;

/// <summary>
/// Model conventions of tenant databases (data-model §2), applied after the entity configurations:
/// <list type="bullet">
/// <item><see cref="IAuditable"/> → <c>created_at</c>, <c>created_by</c>, <c>updated_at</c>, <c>updated_by</c> (uuid of the actor);</item>
/// <item>aggregate roots → <c>xmin</c> concurrency token; domain events are not mapped;</item>
/// <item><see cref="ISoftDeletable"/> → <c>is_deleted</c>, <c>deleted_at</c>, <c>deleted_by</c> + global query filter.</item>
/// </list>
/// </summary>
public static class TenantConventions
{
    public const string CreatedAt = "CreatedAt";
    public const string CreatedBy = "CreatedBy";
    public const string UpdatedAt = "UpdatedAt";
    public const string UpdatedBy = "UpdatedBy";
    public const string IsDeleted = "IsDeleted";
    public const string DeletedAt = "DeletedAt";
    public const string DeletedBy = "DeletedBy";
    public const string Version = "Version";

    public static void Apply(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(type => !type.IsOwned()).ToArray())
        {
            var clrType = entityType.ClrType;
            var entity = modelBuilder.Entity(clrType);

            if (IsAggregateRoot(clrType))
            {
                entity.Ignore(nameof(AggregateRoot<Guid>.DomainEvents));
                entity.Property<uint>(Version).IsRowVersion();
            }

            if (typeof(IAuditable).IsAssignableFrom(clrType))
            {
                entity.Property<DateTimeOffset>(CreatedAt);
                entity.Property<Guid?>(CreatedBy);
                entity.Property<DateTimeOffset?>(UpdatedAt);
                entity.Property<Guid?>(UpdatedBy);
            }

            if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                entity.Property<bool>(IsDeleted).HasDefaultValue(false);
                entity.Property<DateTimeOffset?>(DeletedAt);
                entity.Property<Guid?>(DeletedBy);
                entity.HasQueryFilter(NotDeleted(clrType));
            }
        }
    }

    public static bool IsAggregateRoot(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(AggregateRoot<>))
            {
                return true;
            }
        }

        return false;
    }

    private static LambdaExpression NotDeleted(Type clrType)
    {
        // entity => !EF.Property<bool>(entity, "IsDeleted")
        var entity = Expression.Parameter(clrType, "entity");
        var isDeleted = Expression.Call(typeof(EF), nameof(EF.Property), [typeof(bool)], entity, Expression.Constant(IsDeleted));
        return Expression.Lambda(Expression.Not(isDeleted), entity);
    }
}
