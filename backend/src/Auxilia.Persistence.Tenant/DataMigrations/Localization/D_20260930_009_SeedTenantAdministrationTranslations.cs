namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of tenant administration in the console (S-01, N02): creation, details, status actions, runs, plan, module
/// overrides per role, first Administrator and invitation; errors of the new tenancy and identity codes. EN and IT,
/// idempotent; customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20260930_009_SeedTenantAdministrationTranslations : IDataMigration
{
    public string Key => "D_20260930_009";

    public string Description => "Translations of the tenant administration in the console";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
