namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the web app shell (P3-05): messages of the BFF error codes (<c>errors.AUX-WEB-*</c>), the error alert
/// and the theme switch, in EN and IT. Idempotent; customised translations are kept. Also runs on new tenants.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20260930_005_SeedWebAppTranslations : IDataMigration
{
    public string Key => "D_20260930_005";

    public string Description => "Translations of the web app shell (error codes of the BFF, error alert, theme)";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
