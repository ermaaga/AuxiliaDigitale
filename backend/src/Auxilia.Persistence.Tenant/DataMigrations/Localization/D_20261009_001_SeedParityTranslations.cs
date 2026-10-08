namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>Texts added by the parity verification (H-04): the validity badge of the client's cases (F09). EN and IT, idempotent.</summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261009_001_SeedParityTranslations : IDataMigration
{
    public string Key => "D_20261009_001";

    public string Description => "Translations of the parity verification";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
