using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Identity;

/// <summary>A row of <c>identity.roles</c>: the fixed tenant roles (reference data for FKs and role permissions, P2-03).</summary>
internal sealed class IdentityRoleRow
{
    public TenantRole Name { get; init; }
}

internal sealed class IdentityRoleConfiguration : IEntityTypeConfiguration<IdentityRoleRow>
{
    public void Configure(EntityTypeBuilder<IdentityRoleRow> builder)
    {
        builder.ToTable("roles", TenantSchemas.Identity);
        builder.HasKey(role => role.Name);
        builder.Property(role => role.Name).HasConversion<string>().HasMaxLength(30);
        builder.HasData(Enum.GetValues<TenantRole>().Select(role => new IdentityRoleRow { Name = role }));
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", TenantSchemas.Identity);
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedNever();
        builder.Property(user => user.UserName).HasMaxLength(User.UserNameMaxLength).HasColumnType("citext");
        builder.HasIndex(user => user.UserName).IsUnique();
        builder.Property(user => user.Email).HasMaxLength(User.EmailMaxLength).HasColumnType("citext");
        builder.HasIndex(user => user.Email);
        builder.Property(user => user.LanguageCode).HasMaxLength(User.LanguageMaxLength);
        builder.Property(user => user.PasswordHash).HasMaxLength(500);
        builder.Property(user => user.PasswordFormat).HasConversion<string>().HasMaxLength(20);
        builder.Property(user => user.SecurityStamp).HasMaxLength(User.SecurityStampLength);

        // One account per person; the person lives in the Directory module (reference by id, FK across schemas).
        builder.HasOne<Person>().WithOne().HasForeignKey<User>(user => user.PersonId).OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(user => user.Roles);
        builder.OwnsMany<UserRole>("roles", roles =>
        {
            roles.ToTable("user_roles", TenantSchemas.Identity);
            roles.WithOwner().HasForeignKey(role => role.UserId);
            roles.HasKey(role => new { role.UserId, role.Role });
            roles.Property(role => role.Role).HasConversion<string>().HasMaxLength(30);
            roles.HasOne<IdentityRoleRow>().WithMany().HasForeignKey(role => role.Role).OnDelete(DeleteBehavior.Restrict);
            roles.HasIndex(role => role.Role);
        });
        builder.Navigation("roles").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
