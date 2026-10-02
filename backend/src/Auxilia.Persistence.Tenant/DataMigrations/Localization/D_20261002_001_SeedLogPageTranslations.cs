namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the Log page of the console (S-07, F25, D-28). EN and IT, idempotent;
/// customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261002_001_SeedLogPageTranslations : IDataMigration
{
    public string Key => "D_20261002_001";

    public string Description => "Translations of the Log page of the console";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
