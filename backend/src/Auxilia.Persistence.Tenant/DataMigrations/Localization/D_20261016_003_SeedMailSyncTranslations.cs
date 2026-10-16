namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>Texts of the mail synchronisation (N05, C-06): settings, the "new mail" notification, errors. EN and IT, idempotent.</summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261016_003_SeedMailSyncTranslations : IDataMigration
{
    public string Key => "D_20261016_003";

    public string Description => "Translations of the mail synchronisation";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
