namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the client pages (B-04, F05): list, new client wizard and client 360° (data, employee, specializations,
/// account), plus the user name validation message. EN and IT, idempotent; customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261003_002_SeedClientPagesTranslations : IDataMigration
{
    public string Key => "D_20261003_002";

    public string Description => "Translations of the client pages (list, wizard, detail)";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
