namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>Texts of the mail read live and filed into cases (N05, C-06b): errors and validation. EN and IT, idempotent.</summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261016_004_SeedMailReadingTranslations : IDataMigration
{
    public string Key => "D_20261016_004";

    public string Description => "Translations of the mail reading and filing";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
