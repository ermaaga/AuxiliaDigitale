using Auxilia.Persistence.Tenant.DataMigrations;

namespace Auxilia.Persistence.Tenant.Seed;

/// <summary>
/// Reference data of a brand-new tenant, run once by provisioning (P1-09) after the schema migrations; then the
/// data-migrations it covers are marked as applied (like the legacy <c>SeedMigrator</c>). Only reference data:
/// demo data exists in Development only (quirk Q45). Modules add their seed here as they are ported.
/// </summary>
public sealed class TenantInitialSeed
{
    private readonly DataMigrationRunner dataMigrations;

    public TenantInitialSeed(DataMigrationRunner dataMigrations) => this.dataMigrations = dataMigrations;

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        // Reference data arrives with data-migrations that also run on new tenants (languages and translations:
        // D_20260930_003/004); permissions are synchronised by the tenant migration.
        await dataMigrations.MarkCoveredBySeedAsync(db, cancellationToken);
        await dataMigrations.ApplyPendingAsync(db, cancellationToken);
    }
}
