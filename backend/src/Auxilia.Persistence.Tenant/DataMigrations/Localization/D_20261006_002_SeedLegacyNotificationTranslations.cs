namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the notifications migrated from the legacy application (E-05): the legacy title and message, kept as they were.
/// EN and IT, idempotent; customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261006_002_SeedLegacyNotificationTranslations : IDataMigration
{
    public string Key => "D_20261006_002";

    public string Description => "Translations of the notifications migrated from the legacy application";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
