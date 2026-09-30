namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Keys the backend already hands to clients (F24): module names (<c>modules.*.name</c>), navigation
/// (<c>nav.*</c>), permission descriptions, validation messages (<c>validation.*</c>), <c>errors.generic</c> and
/// language names, in EN and IT. Idempotent; customised translations are kept. Also runs on new tenants.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20260930_004_SeedSystemTranslations : IDataMigration
{
    public string Key => "D_20260930_004";

    public string Description => "Translations of modules, navigation, permissions and validation messages";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
