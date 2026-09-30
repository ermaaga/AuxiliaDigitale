using Auxilia.Application.Abstractions.Settings;

using Microsoft.AspNetCore.DataProtection;

namespace Auxilia.Persistence.Catalog.Configuration;

/// <summary>Data Protection with the key ring stored in the Catalog, shared by Api, Worker and auxctl.</summary>
internal sealed class SettingSecretProtector : ISettingSecretProtector
{
    public const string Purpose = "Auxilia.Configuration.SettingSecret.v1";

    private readonly IDataProtector protector;

    public SettingSecretProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plainValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(plainValue);
        return protector.Protect(plainValue);
    }

    public string Unprotect(string protectedValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(protectedValue);
        return protector.Unprotect(protectedValue);
    }
}
