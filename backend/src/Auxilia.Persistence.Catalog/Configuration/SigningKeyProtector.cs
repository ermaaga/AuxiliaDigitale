using Auxilia.Application.Abstractions.Identity;

using Microsoft.AspNetCore.DataProtection;

namespace Auxilia.Persistence.Catalog.Configuration;

/// <summary>Data Protection for the private keys of the token signing ring.</summary>
internal sealed class SigningKeyProtector : ISigningKeyProtector
{
    public const string Purpose = "Auxilia.Identity.SigningKey.v1";

    private readonly IDataProtector protector;

    public SigningKeyProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string privateKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(privateKey);
        return protector.Protect(privateKey);
    }

    public string Unprotect(string protectedPrivateKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(protectedPrivateKey);
        return protector.Unprotect(protectedPrivateKey);
    }
}
