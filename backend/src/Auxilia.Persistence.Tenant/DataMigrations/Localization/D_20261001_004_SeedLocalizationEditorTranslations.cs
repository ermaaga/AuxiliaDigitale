namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the label and translation editor of the console (S-05, F24). EN and IT, idempotent; customised translations
/// are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261001_004_SeedLocalizationEditorTranslations : IDataMigration
{
    public string Key => "D_20261001_004";

    public string Description => "Translations of the label and translation editor";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
