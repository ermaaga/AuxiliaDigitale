namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the platform console (P3-08, N02): System sign-in with authenticator code, account activation (TOTP
/// enrolment), console shell, tenant list and selector, tenant overview; errors AUX-12040/12041. EN and IT, idempotent;
/// customised translations are kept. Also runs on new tenants (the console reads the seeds through the static bundles).
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20260930_008_SeedPlatformConsoleTranslations : IDataMigration
{
    public string Key => "D_20260930_008";

    public string Description => "Translations of the platform console";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
