namespace Auxilia.Persistence.Tenant.DataMigrations;

/// <summary>
/// A change to reference or configuration data of every tenant (skill auxilia-data-migration, parity F30).
/// Key <c>D_YYYYMMDD_NNN</c>, unique and immutable, mirrored by the class name (<c>D_20260929_001_SeedX</c>).
/// Must be idempotent (natural keys, upserts), tenant-agnostic, must not overwrite tenant customisations and must not
/// call external services.
/// </summary>
public interface IDataMigration
{
    string Key { get; }

    string Description { get; }

    Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken);
}

/// <summary>
/// <c>false</c> = the initial seed of a new tenant does not cover this data-migration, so it also runs on new tenants.
/// Without the attribute a data-migration is considered covered by the seed.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class IncludedInInitialSeedAttribute(bool included) : Attribute
{
    public bool Included { get; } = included;
}
