using Auxilia.Application.Abstractions.Channels;

using Microsoft.AspNetCore.DataProtection;

namespace Auxilia.Persistence.Catalog.Configuration;

/// <summary>Data Protection (Catalog key ring) for sending-account secrets, separate purpose from settings and connections.</summary>
internal sealed class AccountSecretProtector : IAccountSecretProtector
{
    public const string Purpose = "Auxilia.Messaging.AccountSecret.v1";

    private readonly IDataProtector protector;

    public AccountSecretProtector(IDataProtectionProvider provider)
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
