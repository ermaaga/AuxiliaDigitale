using Azure.Core;
using Azure.Identity;

namespace Auxilia.Persistence.Catalog;

/// <summary>
/// The Key Vault key that protects the Data Protection key ring at rest (#141, R4): set on Azure by the Bicep
/// (<c>DataProtection__KeyVaultKeyId</c> and the <c>AZURE_CLIENT_ID</c> of the runtime identity), never used in
/// Development, where the keys of a local database stay unprotected.
/// </summary>
/// <param name="KeyId">Key identifier, without version (the vault wraps with the newest version, each key records its own).</param>
/// <param name="ManagedIdentityClientId">Client id of the user-assigned identity; <see langword="null"/> for the system-assigned one.</param>
public sealed record DataProtectionKeyVault(Uri KeyId, string? ManagedIdentityClientId)
{
    public const string KeyIdSetting = "DataProtection:KeyVaultKeyId";

    /// <summary>Read by Azure.Identity too: the identity Container Apps assigns to Api, Worker and the auxctl job.</summary>
    public const string ManagedIdentityClientIdSetting = "AZURE_CLIENT_ID";

    /// <summary>
    /// The configured key, or <see langword="null"/> in Development (always) or when no key is configured (local hosts,
    /// tests). A key that is not an https URI fails the start.
    /// </summary>
    public static DataProtectionKeyVault? From(string? keyId, string? managedIdentityClientId, bool isDevelopment)
    {
        if (isDevelopment || string.IsNullOrWhiteSpace(keyId))
        {
            return null;
        }

        if (!Uri.TryCreate(keyId.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"{KeyIdSetting} must be the https identifier of a Key Vault key.");
        }

        return new DataProtectionKeyVault(uri, string.IsNullOrWhiteSpace(managedIdentityClientId) ? null : managedIdentityClientId.Trim());
    }

    /// <summary>Managed identity only: no developer or environment credentials are tried on Azure.</summary>
    internal TokenCredential Credential() => new ManagedIdentityCredential(
        ManagedIdentityClientId is { } clientId ? ManagedIdentityId.FromUserAssignedClientId(clientId) : ManagedIdentityId.SystemAssigned);
}
