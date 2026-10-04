namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the request, notification and session pages (B-21, F15–F17): inbox and thread, bell and notification
/// page with the preferences, active sessions.
/// EN and IT, idempotent; customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261004_003_SeedRequestNotificationSessionPagesTranslations : IDataMigration
{
    public string Key => "D_20261004_003";

    public string Description => "Translations of the request, notification and session pages";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
