using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Tenancy;

using Microsoft.AspNetCore.DataProtection;

namespace Auxilia.Persistence.Catalog.Configuration;

/// <summary>Data Protection for the TOTP secrets of the users of a tenant (N04): the tenant slug is part of the purpose.</summary>
internal sealed class UserTwoFactorSecretProtector : IUserTwoFactorSecretProtector
{
    public const string Purpose = "Auxilia.Tenant.TwoFactorSecret.v1";

    private readonly IDataProtectionProvider provider;
    private readonly ITenantContext tenantContext;

    public UserTwoFactorSecretProtector(IDataProtectionProvider provider, ITenantContext tenantContext)
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
