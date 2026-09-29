using Auxilia.Domain.Platform;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Catalog.Configurations.Platform;

/// <summary>Stores <see cref="TenantRole"/> arrays as <c>text[]</c> of role names.</summary>
internal static class RoleArrayConversion
{
    private static readonly ValueComparer<TenantRole[]> Comparer = new(
        (left, right) => (left ?? Array.Empty<TenantRole>()).SequenceEqual(right ?? Array.Empty<TenantRole>()),
        roles => roles.Aggregate(0, (hash, role) => HashCode.Combine(hash, role)),
        roles => roles.ToArray());

    public static PropertyBuilder<TenantRole[]> HasRoleArrayConversion(this PropertyBuilder<TenantRole[]> property) =>
        property
            .HasConversion(
                roles => roles.Select(role => role.ToString()).ToArray(),
                names => names.Select(Enum.Parse<TenantRole>).ToArray(),
                Comparer)
            .HasColumnType("text[]");
}
