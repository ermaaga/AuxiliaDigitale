namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Texts of the messaging page of the console (S-03, N03): sending accounts, test send, sender rules, outbound log and
/// the messaging error codes it shows. EN and IT, idempotent; customised translations are kept.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261001_002_SeedMessagingConsoleTranslations : IDataMigration
{
    public string Key => "D_20261001_002";

    public string Description => "Translations of the messaging page of the console";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
