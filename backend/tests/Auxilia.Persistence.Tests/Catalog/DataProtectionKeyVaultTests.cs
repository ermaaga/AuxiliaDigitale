using Auxilia.Persistence.Catalog;

using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Auxilia.Persistence.Tests.Catalog;

/// <summary>Key Vault protection of the Data Protection keys (#141): never in Development, only with a configured key.</summary>
public sealed class DataProtectionKeyVaultTests
{
    private const string KeyId = "https://kv-auxilia-next-qa.vault.azure.net/keys/dataprotection-kek";

    [Fact]
    public void From_InDevelopment_IsNeverUsed_EvenWithAKey()
    {
        DataProtectionKeyVault.From(KeyId, "client", isDevelopment: true).ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void From_WithoutAKey_IsNotUsed(string? keyId)
    {
        DataProtectionKeyVault.From(keyId, "client", isDevelopment: false).ShouldBeNull();
    }

    [Theory]
    [InlineData("dataprotection-kek")]
    [InlineData("http://kv-auxilia-next-qa.vault.azure.net/keys/dataprotection-kek")]
    public void From_KeyThatIsNotAnHttpsUri_FailsTheStart(string keyId)
    {
        Should.Throw<InvalidOperationException>(() => DataProtectionKeyVault.From(keyId, null, isDevelopment: false));
    }

    [Fact]
    public void From_ConfiguredKey_IsUsedWithTheManagedIdentity()
    {
        var keyVault = DataProtectionKeyVault.From($" {KeyId} ", " 00000000-0000-0000-0000-000000000001 ", isDevelopment: false)!;

        keyVault.KeyId.ShouldBe(new Uri(KeyId));
        keyVault.ManagedIdentityClientId.ShouldBe("00000000-0000-0000-0000-000000000001");
        DataProtectionKeyVault.From(KeyId, " ", isDevelopment: false)!.ManagedIdentityClientId.ShouldBeNull();
    }

    [Fact]
    public void AddCatalogPersistence_WithAKey_EncryptsTheKeyRingWithKeyVault_WithoutOneDoesNot()
    {
        // Resolving the options builds the encryptor only: no call reaches Azure.
        Encryptor(new DataProtectionKeyVault(new Uri(KeyId), null)).ShouldNotBeNull().GetType().Name.ShouldContain("AzureKeyVault");
        Encryptor(null).ShouldBeNull();
    }

    private static object? Encryptor(DataProtectionKeyVault? keyVault)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatalogPersistence("Host=localhost;Database=unused", keyVault);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlEncryptor;
    }
}
