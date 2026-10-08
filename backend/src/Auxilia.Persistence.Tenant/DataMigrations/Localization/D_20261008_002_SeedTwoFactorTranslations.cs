namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>Texts of the authenticator app of the tenant users and "stay signed in" (N04). EN and IT, idempotent; customised translations are kept.</summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261008_002_SeedTwoFactorTranslations : IDataMigration
{
    public string Key => "D_20261008_002";

    public string Description => "Translations of the authenticator app of the tenant users and of stay signed in";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
