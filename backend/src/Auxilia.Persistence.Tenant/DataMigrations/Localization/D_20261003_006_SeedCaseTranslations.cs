namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the cases (B-08, F09, F10): the delete permission, the validation messages of opening, payments and
/// completion, and the errors of the workflow. EN and IT, idempotent; customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261003_006_SeedCaseTranslations : IDataMigration
{
    public string Key => "D_20261003_006";

    public string Description => "Translations of the cases";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
