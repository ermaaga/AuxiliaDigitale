namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the employee pages (B-05, F06: list, new employee, detail with workload, default employee, administrator,
/// clients in charge, specializations and account) and of the own profile (F04: picture, personal data, language and
/// theme, password). EN and IT, idempotent; customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261003_003_SeedEmployeeAndProfilePagesTranslations : IDataMigration
{
    public string Key => "D_20261003_003";

    public string Description => "Translations of the employee and profile pages";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
