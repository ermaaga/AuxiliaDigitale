using Auxilia.Domain.Identity;
using Auxilia.Domain.Platform;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Catalog.Configurations.Platform;

/// <summary>Console sessions of platform users: the same session and refresh-token rules as tenant users (P2-06).</summary>
internal sealed class PlatformSessionConfiguration : IEntityTypeConfiguration<RefreshSession>
{
    public void Configure(EntityTypeBuilder<RefreshSession> builder)
    {
        builder.ToTable("platform_sessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();
        builder.Property(session => session.ClientId).HasMaxLength(RefreshSession.ClientIdMaxLength);
        builder.Property(session => session.SecurityStamp).HasMaxLength(PlatformUser.SecurityStampLength);
        builder.Property(session => session.IpAddress).HasMaxLength(RefreshSession.IpMaxLength);
        builder.Property(session => session.UserAgent).HasMaxLength(RefreshSession.UserAgentMaxLength);
        builder.Property(session => session.EndReason).HasConversion<string>().HasMaxLength(30);
        builder.Ignore(session => session.DomainEvents);
        builder.HasOne<PlatformUser>().WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(session => new { session.UserId, session.EndedAt });
    }
}

internal sealed class PlatformRefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("platform_refresh_tokens");
        builder.HasKey(token => token.TokenHash);
        builder.Property(token => token.TokenHash).HasMaxLength(RefreshToken.HashLength);
        builder.HasOne<RefreshSession>().WithMany().HasForeignKey(token => token.SessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(token => token.SessionId);
    }
}

/// <summary>One-use activation tokens of platform users (printed by <c>auxctl platform users add|reset</c>).</summary>
internal sealed class PlatformUserTokenConfiguration : IEntityTypeConfiguration<UserToken>
{
    public void Configure(EntityTypeBuilder<UserToken> builder)
    {
        builder.ToTable("platform_user_tokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedNever();
        builder.Property(token => token.Purpose).HasConversion<string>().HasMaxLength(20);
        builder.Property(token => token.TokenHash).HasMaxLength(RefreshToken.HashLength);
        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => new { token.UserId, token.Purpose });
        builder.HasOne<PlatformUser>().WithMany().HasForeignKey(token => token.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
