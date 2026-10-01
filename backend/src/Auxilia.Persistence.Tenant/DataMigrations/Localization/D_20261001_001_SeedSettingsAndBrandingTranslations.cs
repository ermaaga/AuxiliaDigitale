namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the settings editor and the branding pages of the console (S-02, F23): a description for every setting,
/// the branding form and its validation messages. EN and IT, idempotent; customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261001_001_SeedSettingsAndBrandingTranslations : IDataMigration
{
    public string Key => "D_20261001_001";

    public string Description => "Translations of the settings editor and the branding pages";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
