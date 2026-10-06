namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>Texts of the console page of the recurring jobs (N02, D-15). EN and IT, idempotent; customised translations are kept.</summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261006_003_SeedJobConsoleTranslations : IDataMigration
{
    public string Key => "D_20261006_003";

    public string Description => "Translations of the console page of the recurring jobs";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
