namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>The message of an unknown list filter (F28: refused, never ignored). EN and IT, idempotent.</summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261006_004_SeedListFilterTranslations : IDataMigration
{
    public string Key => "D_20261006_004";

    public string Description => "Translation of the unknown list filter error";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
