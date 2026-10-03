namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the document pages (B-13, F14): list, uploader, detail drawer and areas. EN and IT, idempotent;
/// customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261003_011_SeedDocumentPagesTranslations : IDataMigration
{
    public string Key => "D_20261003_011";

    public string Description => "Translations of the document pages";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
