namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>Texts of the connected calendars in the appointment calendar (N05, C-05): legend, sync bar, event details. EN and IT, idempotent.</summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261016_002_SeedExternalCalendarTranslations : IDataMigration
{
    public string Key => "D_20261016_002";

    public string Description => "Translations of the connected calendars in the appointment calendar";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
