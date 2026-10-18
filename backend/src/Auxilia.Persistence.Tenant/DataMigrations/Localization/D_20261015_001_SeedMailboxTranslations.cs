namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>Texts of connected accounts (N05, C-02): module, permissions, setting, validation and errors. EN and IT, idempotent.</summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261015_001_SeedMailboxTranslations : IDataMigration
{
    public string Key => "D_20261015_001";

    public string Description => "Translations of connected accounts";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
