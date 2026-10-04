namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the case expiry job and reminder (B-25, F11): notifications, errors, the case action.
/// EN and IT, idempotent; customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261004_008_SeedCaseExpiryTranslations : IDataMigration
{
    public string Key => "D_20261004_008";

    public string Description => "Translations of the case expiry notifications and reminder";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
