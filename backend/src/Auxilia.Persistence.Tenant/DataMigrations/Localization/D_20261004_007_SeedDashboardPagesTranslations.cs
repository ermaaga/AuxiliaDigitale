namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the dashboard, exports and clients overview pages (B-24, F27, F26, F07).
/// EN and IT, idempotent; customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261004_007_SeedDashboardPagesTranslations : IDataMigration
{
    public string Key => "D_20261004_007";

    public string Description => "Translations of the dashboard, exports and clients overview pages";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
