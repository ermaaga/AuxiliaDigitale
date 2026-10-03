namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the case pages (B-14, F09, F33): list, quick creation, detail with stepper, completion, payments,
/// timeline and case folders. EN and IT, idempotent; customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261003_012_SeedCasePagesTranslations : IDataMigration
{
    public string Key => "D_20261003_012";

    public string Description => "Translations of the case pages";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
