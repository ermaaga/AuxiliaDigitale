namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of employee management (B-02, F06): employee grid, validation and employee errors. EN and IT, idempotent;
/// customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261002_003_SeedEmployeeTranslations : IDataMigration
{
    public string Key => "D_20261002_003";

    public string Description => "Translations of the employee management (grid, validation, errors)";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
