using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Platform;

/// <summary>
/// Tenant provisioning (skill auxilia-tenant-provisioning): needs the database-admin login, so only auxctl and the Worker
/// register it. Status changes, plans and overrides are in <see cref="IPlatformTenantManager"/>.
/// </summary>
public interface ITenantLifecycleManager
{
    /// <summary>
    /// Resumable and idempotent: a tenant left in Provisioning or MigrationFailed is resumed; an existing tenant in
    /// any other status is a conflict. Steps: catalog row + default plan → database → protected secret → schema +
    /// initial seed → Active, with a <c>catalog.migration_runs</c> row.
    /// </summary>
    Task<Result<TenantInfo>> ProvisionAsync(ProvisionTenant request, CancellationToken cancellationToken);

    /// <summary>
    /// Provisioning of a tenant the console already created (Worker, <c>ProvisionTenantCommand</c>): resumes it when it
    /// is in Provisioning or MigrationFailed; a tenant already provisioned is left as it is (redelivered message).
    /// </summary>
    Task<Result<TenantInfo>> ResumeProvisioningAsync(string slug, CancellationToken cancellationToken);
}

/// <summary>Schema and data migrations of the Catalog and of tenant databases (auxctl).</summary>
public interface ITenantMigrationManager
{
    Task<Result<IReadOnlyList<string>>> MigrateCatalogAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Migrates one tenant (Active, Suspended or MigrationFailed). On failure the tenant becomes MigrationFailed (a
    /// suspended tenant stays suspended), the run is recorded as failed and the exception is rethrown.
    /// </summary>
    Task<Result<TenantDatabaseVersion>> MigrateTenantAsync(string slug, CancellationToken cancellationToken);

    /// <summary>Slugs of the tenants that <c>migrate tenants --all</c> processes.</summary>
    Task<IReadOnlyList<string>> MigratableTenantsAsync(CancellationToken cancellationToken);
}
