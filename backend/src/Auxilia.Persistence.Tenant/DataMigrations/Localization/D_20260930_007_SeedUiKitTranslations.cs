namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the UI kit (P3-07): data tables (paging, sorting, columns, export, empty states), combobox and confirmation
/// dialog, and of the login audit page (columns, sign-in methods, failure reasons), in EN and IT. Idempotent;
/// customised translations are kept. Also runs on new tenants.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20260930_007_SeedUiKitTranslations : IDataMigration
{
    public string Key => "D_20260930_007";

    public string Description => "Translations of the UI kit and of the login audit page";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
