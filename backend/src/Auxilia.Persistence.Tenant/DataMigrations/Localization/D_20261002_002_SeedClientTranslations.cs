namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of client management (B-01, F05): new permissions, client grid, person validation and client errors. EN and IT, idempotent;
/// customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261002_002_SeedClientTranslations : IDataMigration
{
    public string Key => "D_20261002_002";

    public string Description => "Translations of the client management (permissions, grid, validation)";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
