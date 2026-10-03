namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the appointments (B-16, F13): the grid name, the validation messages of scheduling, requests and moves,
/// and the errors of the workflow. EN and IT, idempotent; customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261003_014_SeedAppointmentTranslations : IDataMigration
{
    public string Key => "D_20261003_014";

    public string Description => "Translations of the appointments";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
