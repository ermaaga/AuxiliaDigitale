namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the marketing pages (M-04, N01): campaigns, segment builder, lists, templates, exclusions.
/// EN and IT, idempotent; customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261006_001_SeedMarketingPagesTranslations : IDataMigration
{
    public string Key => "D_20261006_001";

    public string Description => "Translations of the marketing pages";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
