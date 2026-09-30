namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the public pages and of the tenant app shell (P3-06): messages of the sign-in and account-link errors,
/// the sign-in, password and activation pages and the navigation shell, in EN and IT. Idempotent; customised
/// translations are kept. Also runs on new tenants.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20260930_006_SeedAuthAndShellTranslations : IDataMigration
{
    public string Key => "D_20260930_006";

    public string Description => "Translations of the sign-in pages and of the app shell";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
