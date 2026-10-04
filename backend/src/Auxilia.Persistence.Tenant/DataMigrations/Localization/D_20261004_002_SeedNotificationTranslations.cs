namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the notifications (B-19, F16): title and message of every kind (also used by the e-mails), the
/// permission, the validation message of the preferences and the errors. EN and IT, idempotent; customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261004_002_SeedNotificationTranslations : IDataMigration
{
    public string Key => "D_20261004_002";

    public string Description => "Translations of the notifications";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
