using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Application.Abstractions.Tenancy;

using Microsoft.AspNetCore.DataProtection;

namespace Auxilia.Persistence.Catalog.Configuration;

/// <summary>
/// Data Protection for the secrets of connected accounts (N05, ADR 0021): OAuth refresh tokens, IMAP passwords and PKCE
/// verifiers. The tenant slug is part of the purpose, so a value copied to another tenant cannot be read.
/// </summary>
internal sealed class ConnectedAccountSecretProtector : IConnectedAccountSecretProtector
{
    public const string Purpose = "Auxilia.Mailbox.AccountSecret.v1";

    private readonly IDataProtectionProvider provider;
    private readonly ITenantContext tenantContext;

    public ConnectedAccountSecretProtector(IDataProtectionProvider provider, ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(provider);
        this.provider = provider;
        this.tenantContext = tenantContext;
    }

    public string Protect(string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        return Protector().Protect(secret);
    }

    public string Unprotect(string protectedSecret)
    {
        ArgumentException.ThrowIfNullOrEmpty(protectedSecret);
        return Protector().Unprotect(protectedSecret);
    }

    private IDataProtector Protector() => provider.CreateProtector(Purpose, tenantContext.Tenant.Slug);
}
