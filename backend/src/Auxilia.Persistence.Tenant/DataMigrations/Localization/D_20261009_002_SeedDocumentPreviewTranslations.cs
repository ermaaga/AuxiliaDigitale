namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>Texts of the document preview in the drawer (F14, ADR 0020): PDF pages, text files, fallbacks. EN and IT, idempotent.</summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261009_002_SeedDocumentPreviewTranslations : IDataMigration
{
    public string Key => "D_20261009_002";

    public string Description => "Translations of the document preview";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
