namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the appointment pages (B-17, F13): calendar, grid, drawer, schedule and request forms.
/// EN and IT, idempotent; customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261003_015_SeedAppointmentPagesTranslations : IDataMigration
{
    public string Key => "D_20261003_015";

    public string Description => "Translations of the appointment pages";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
