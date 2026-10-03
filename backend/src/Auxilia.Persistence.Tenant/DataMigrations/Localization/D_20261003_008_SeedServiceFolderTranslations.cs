namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the folder templates of the services (B-10, F33): validation messages and errors. EN and IT, idempotent;
/// customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261003_008_SeedServiceFolderTranslations : IDataMigration
{
    public string Key => "D_20261003_008";

    public string Description => "Translations of the service folders";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
