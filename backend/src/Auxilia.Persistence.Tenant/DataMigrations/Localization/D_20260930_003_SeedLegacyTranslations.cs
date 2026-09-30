namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>
/// Languages EN and IT and the legacy dictionary (F24, <c>LocalizationSeedData</c> of the baseline incl. the security
/// keys of <c>Security_Update</c>) with the original key names, so tenant customisations imported from the legacy
/// database (E-02) match. Workout keys are left out (D-09). Idempotent; customised translations are kept. Also runs on
/// new tenants (not covered by the initial seed).
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20260930_003_SeedLegacyTranslations : IDataMigration
{
    public string Key => "D_20260930_003";

    public string Description => "Languages EN/IT and legacy translation keys";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
