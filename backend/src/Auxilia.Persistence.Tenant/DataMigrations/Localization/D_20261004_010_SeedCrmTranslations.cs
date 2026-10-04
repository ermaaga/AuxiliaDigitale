namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the CRM extensions (B-26): tasks, client timeline, document checklists.
/// EN and IT, idempotent; customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261004_010_SeedCrmTranslations : IDataMigration
{
    public string Key => "D_20261004_010";

    public string Description => "Translations of tasks, timeline and checklists";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
