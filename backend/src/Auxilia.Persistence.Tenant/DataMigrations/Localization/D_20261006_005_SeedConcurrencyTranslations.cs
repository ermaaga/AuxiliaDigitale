namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>Messages of the optimistic concurrency errors (F29: 409, 412, 428). EN and IT, idempotent.</summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261006_005_SeedConcurrencyTranslations : IDataMigration
{
    public string Key => "D_20261006_005";

    public string Description => "Translations of the optimistic concurrency errors";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
