namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of external registration (B-06, F02/F03, API only D-14): the minimum age setting (Q59), the validation
/// messages of a request and the errors an external client application shows. EN and IT, idempotent; customised
/// translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261003_004_SeedRegistrationTranslations : IDataMigration
{
    public string Key => "D_20261003_004";

    public string Description => "Translations of external registration";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
