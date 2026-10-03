namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the own profile (B-03, F04): language, picture and theme validation, picture and session errors. EN and IT,
/// idempotent; customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261003_001_SeedProfileTranslations : IDataMigration
{
    public string Key => "D_20261003_001";

    public string Description => "Translations of the own profile (validation, errors)";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
