using Auxilia.Application.Abstractions.Identity;

using Microsoft.AspNetCore.DataProtection;

namespace Auxilia.Persistence.Catalog.Configuration;

/// <summary>Data Protection for the TOTP secrets of platform users.</summary>
internal sealed class TwoFactorSecretProtector : ITwoFactorSecretProtector
{
    public const string Purpose = "Auxilia.Platform.TwoFactorSecret.v1";

    private readonly IDataProtector protector;

    public TwoFactorSecretProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        return protector.Protect(secret);
    }

    public string Unprotect(string protectedSecret)
    {
        ArgumentException.ThrowIfNullOrEmpty(protectedSecret);
        return protector.Unprotect(protectedSecret);
    }
}
