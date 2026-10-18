namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>Error text of a case ZIP over the download limit (issue #139). EN and IT, idempotent.</summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261018_001_SeedZipLimitTranslations : IDataMigration
{
    public string Key => "D_20261018_001";

    public string Description => "Translation of the case ZIP limit error";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
