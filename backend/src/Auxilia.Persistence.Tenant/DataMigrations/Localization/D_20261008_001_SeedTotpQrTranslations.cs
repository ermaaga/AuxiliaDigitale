namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>Texts of the QR code of the System user activation (N02, D-22). EN and IT, idempotent; customised translations are kept.</summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261008_001_SeedTotpQrTranslations : IDataMigration
{
    public string Key => "D_20261008_001";

    public string Description => "Translations of the QR code of the System user activation";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
