namespace Auxilia.Application.Abstractions.Tenancy;

/// <summary>Reads tenants from the Catalog (cached briefly).</summary>
public interface ITenantDirectory
{
    Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken cancellationToken);

    /// <summary>Tenant of a custom domain registered in <c>catalog.tenant_domains</c>.</summary>
    Task<TenantInfo?> FindByHostAsync(string host, CancellationToken cancellationToken);

    /// <summary>The protected connection string of the tenant database, or null while provisioning.</summary>
    Task<string?> GetProtectedConnectionStringAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>Encrypts tenant connection strings with Data Protection (purpose <c>Auxilia.Tenancy.ConnectionString.v1</c>).</summary>
public interface ITenantConnectionProtector
{
    string Protect(string connectionString);

    string Unprotect(string protectedConnectionString);
}
