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

        // F35: hashes of the last passwords (at most User.MaxPasswordHistory), loaded with the user.
        builder.Ignore(user => user.PasswordHistory);
        builder.OwnsMany<PasswordHistoryEntry>("passwordHistory", history =>
        {
            history.ToTable("password_history", TenantSchemas.Identity);
            history.WithOwner().HasForeignKey(entry => entry.UserId);
            history.HasKey(entry => entry.Id);
            history.Property(entry => entry.Id).ValueGeneratedNever();
            history.Property(entry => entry.PasswordHash).HasMaxLength(500);
            history.Property(entry => entry.Format).HasConversion<string>().HasMaxLength(20);
            history.HasIndex(entry => new { entry.UserId, entry.CreatedAt });
        });
        builder.Navigation("passwordHistory").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<PermissionEntry>
{
    public void Configure(EntityTypeBuilder<PermissionEntry> builder)
    {
        builder.ToTable("permissions", TenantSchemas.Identity);
        builder.HasKey(permission => permission.Code);
        builder.Property(permission => permission.Code).HasMaxLength(PermissionEntry.CodeMaxLength);
        builder.Property(permission => permission.ModuleCode).HasMaxLength(PermissionEntry.ModuleCodeMaxLength);
        builder.HasIndex(permission => permission.ModuleCode);
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RoleGrant>
{
    public void Configure(EntityTypeBuilder<RoleGrant> builder)
    {
        builder.ToTable("role_permissions", TenantSchemas.Identity);
        builder.HasKey(grant => new { grant.Role, grant.PermissionCode });
        builder.Property(grant => grant.Role).HasConversion<string>().HasMaxLength(30);
        builder.Property(grant => grant.PermissionCode).HasMaxLength(PermissionEntry.CodeMaxLength);
        builder.HasOne<IdentityRoleRow>().WithMany().HasForeignKey(grant => grant.Role).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PermissionEntry>().WithMany().HasForeignKey(grant => grant.PermissionCode).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(grant => grant.PermissionCode);
    }
}

internal sealed class LoginAttemptConfiguration : IEntityTypeConfiguration<LoginAttempt>
{
    public void Configure(EntityTypeBuilder<LoginAttempt> builder)
    {
        builder.ToTable("login_attempts", TenantSchemas.Identity);
        builder.HasKey(attempt => attempt.Id);
        builder.Property(attempt => attempt.Id).ValueGeneratedNever();
        builder.Property(attempt => attempt.UserName).HasMaxLength(LoginAttempt.UserNameMaxLength).HasColumnType("citext");
        builder.Property(attempt => attempt.Method).HasMaxLength(LoginAttempt.MethodMaxLength);
        builder.Property(attempt => attempt.FailureReason).HasMaxLength(LoginAttempt.ReasonMaxLength);
        builder.Property(attempt => attempt.IpAddress).HasMaxLength(RefreshSession.IpMaxLength);
        builder.Property(attempt => attempt.UserAgent).HasMaxLength(RefreshSession.UserAgentMaxLength);

        // No FK to users: attempts with unknown user names are recorded too, and the audit outlives deleted accounts.
        builder.HasIndex(attempt => attempt.AttemptedAt);
        builder.HasIndex(attempt => new { attempt.UserName, attempt.AttemptedAt });
        builder.HasIndex(attempt => attempt.UserId);
    }
}
