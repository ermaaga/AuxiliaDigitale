using Auxilia.Application.Abstractions.Tenancy;

using Microsoft.AspNetCore.DataProtection;

namespace Auxilia.Persistence.Catalog.Tenancy;

/// <summary>Data Protection with the key ring stored in the Catalog (skill auxilia-multitenancy).</summary>
internal sealed class TenantConnectionProtector : ITenantConnectionProtector
{
    public const string Purpose = "Auxilia.Tenancy.ConnectionString.v1";

    private readonly IDataProtector protector;

    public TenantConnectionProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return protector.Protect(connectionString);
    }

    public string Unprotect(string protectedConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedConnectionString);
        return protector.Unprotect(protectedConnectionString);
    }
}
