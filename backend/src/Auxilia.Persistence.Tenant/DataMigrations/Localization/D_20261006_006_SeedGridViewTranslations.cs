namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>Texts of the personal views of the lists (F21). EN and IT, idempotent; customised translations are kept.</summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261006_006_SeedGridViewTranslations : IDataMigration
{
    public string Key => "D_20261006_006";

    public string Description => "Translations of the personal views of the lists";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
