namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of templates, campaigns and suppressions (M-03, N01): validation, errors, permissions.
/// EN and IT, idempotent; customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261005_001_SeedCampaignTranslations : IDataMigration
{
    public string Key => "D_20261005_001";

    public string Description => "Translations of templates, campaigns and suppressions";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
