namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>Texts of the mailbox synchronisation (N05, C-04): settings, the "reconnect" notification, errors. EN and IT, idempotent.</summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261016_001_SeedMailboxSyncTranslations : IDataMigration
{
    public string Key => "D_20261016_001";

    public string Description => "Translations of the mailbox synchronisation";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
