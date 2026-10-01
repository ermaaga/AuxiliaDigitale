namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of grid layouts and custom fields (S-04, F20, F21): console pages, entity and grid names, validation messages.
/// EN and IT, idempotent; customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261001_003_SeedCustomizationTranslations : IDataMigration
{
    public string Key => "D_20261001_003";

    public string Description => "Translations of grid layouts and custom fields";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
