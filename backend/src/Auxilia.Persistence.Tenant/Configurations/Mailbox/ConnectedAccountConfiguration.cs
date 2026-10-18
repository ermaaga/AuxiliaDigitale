using Auxilia.Domain.Identity;
using Auxilia.Domain.Mailbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Mailbox;

internal sealed class ConnectedAccountConfiguration : IEntityTypeConfiguration<ConnectedAccount>
{
    public void Configure(EntityTypeBuilder<ConnectedAccount> builder)
    {
        builder.ToTable("connected_accounts", TenantSchemas.Mailbox, table =>
        {
            table.HasCheckConstraint("ck_connected_accounts_provider", "provider IN ('Google', 'Microsoft', 'Imap')");
            table.HasCheckConstraint("ck_connected_accounts_status", "status IN ('Active', 'NeedsReconnect')");
            table.HasCheckConstraint("ck_connected_accounts_capability", "has_mail OR has_calendar");
            table.HasCheckConstraint(
                "ck_connected_accounts_imap",
                "(provider = 'Imap') = (imap_host IS NOT NULL AND imap_port IS NOT NULL AND imap_security IS NOT NULL AND imap_user_name IS NOT NULL)");
        });
        builder.HasKey(account => account.Id);
        builder.Property(account => account.Id).ValueGeneratedNever();
        builder.Property(account => account.Provider).HasConversion<string>().HasMaxLength(20);
        builder.Property(account => account.Address).HasMaxLength(ConnectedAccount.AddressMaxLength);
        builder.Property(account => account.DisplayName).HasMaxLength(ConnectedAccount.DisplayNameMaxLength);
        builder.Property(account => account.Color).HasMaxLength(20);
        builder.Property(account => account.GrantedScopes).HasMaxLength(ConnectedAccount.ScopesMaxLength);
        builder.Property(account => account.ImapHost).HasMaxLength(ConnectedAccount.HostMaxLength);
        builder.Property(account => account.ImapSecurity).HasConversion<string>().HasMaxLength(20);
        builder.Property(account => account.ImapUserName).HasMaxLength(ConnectedAccount.UserNameMaxLength);
        builder.Property(account => account.ProtectedSecret);
        builder.Property(account => account.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(account => account.LastErrorCode).HasMaxLength(ConnectedAccount.ErrorCodeMaxLength);
        builder.HasOne<User>().WithMany().HasForeignKey(account => account.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(account => new { account.UserId, account.Provider, account.Address }).IsUnique();
    }
}

internal sealed class OAuthRequestConfiguration : IEntityTypeConfiguration<OAuthRequest>
{
    public void Configure(EntityTypeBuilder<OAuthRequest> builder)
    {
        builder.ToTable("oauth_requests", TenantSchemas.Mailbox, table =>
            table.HasCheckConstraint("ck_oauth_requests_provider", "provider IN ('Google', 'Microsoft')"));
        builder.HasKey(request => request.Id);
        builder.Property(request => request.Id).ValueGeneratedNever();
        builder.Property(request => request.StateHash).HasMaxLength(OAuthRequest.StateHashLength).IsFixedLength();
        builder.Property(request => request.Provider).HasConversion<string>().HasMaxLength(20);
        builder.Property(request => request.ProtectedVerifier);
        builder.HasOne<User>().WithMany().HasForeignKey(request => request.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(request => request.StateHash).IsUnique();
        builder.HasIndex(request => request.UserId);
        builder.HasIndex(request => request.ExpiresAt);
    }
}
