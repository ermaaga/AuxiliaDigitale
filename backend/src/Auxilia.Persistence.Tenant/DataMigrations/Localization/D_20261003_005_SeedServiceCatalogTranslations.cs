namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the service catalog (B-07, F08, Q26): the new read permission, the service grid, the validation messages
/// and the errors of categories and services. EN and IT, idempotent; customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261003_005_SeedServiceCatalogTranslations : IDataMigration
{
    public string Key => "D_20261003_005";

    public string Description => "Translations of the service catalog";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
