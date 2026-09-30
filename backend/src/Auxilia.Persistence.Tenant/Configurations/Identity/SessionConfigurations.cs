using Auxilia.Domain.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Identity;

internal sealed class RefreshSessionConfiguration : IEntityTypeConfiguration<RefreshSession>
{
    public void Configure(EntityTypeBuilder<RefreshSession> builder)
    {
        builder.ToTable("refresh_sessions", TenantSchemas.Identity);
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();
        builder.Property(session => session.ClientId).HasMaxLength(RefreshSession.ClientIdMaxLength);
        builder.Property(session => session.SecurityStamp).HasMaxLength(User.SecurityStampLength);
        builder.Property(session => session.IpAddress).HasMaxLength(RefreshSession.IpMaxLength);
        builder.Property(session => session.UserAgent).HasMaxLength(RefreshSession.UserAgentMaxLength);
        builder.Property(session => session.EndReason).HasConversion<string>().HasMaxLength(30);
        builder.HasOne<User>().WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);

        // Open sessions of a user (single session, reset password, F17 list).
        builder.HasIndex(session => new { session.UserId, session.EndedAt });
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens", TenantSchemas.Identity);
        builder.HasKey(token => token.TokenHash);
        builder.Property(token => token.TokenHash).HasMaxLength(RefreshToken.HashLength);
        builder.HasOne<RefreshSession>().WithMany().HasForeignKey(token => token.SessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(token => token.SessionId);
    }
}

internal sealed class UserTokenConfiguration : IEntityTypeConfiguration<UserToken>
{
    public void Configure(EntityTypeBuilder<UserToken> builder)
    {
        builder.ToTable("user_tokens", TenantSchemas.Identity);
        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedNever();
        builder.Property(token => token.Purpose).HasConversion<string>().HasMaxLength(20);
        builder.Property(token => token.TokenHash).HasMaxLength(RefreshToken.HashLength);
        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => new { token.UserId, token.Purpose });
        builder.HasOne<User>().WithMany().HasForeignKey(token => token.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
