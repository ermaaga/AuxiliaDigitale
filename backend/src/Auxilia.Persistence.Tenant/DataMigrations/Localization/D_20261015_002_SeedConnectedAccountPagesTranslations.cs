namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>Texts of the profile tab "Connected accounts" (N05, C-03): buttons, account list, OAuth and PEC / IMAP dialogs. EN and IT, idempotent.</summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261015_002_SeedConnectedAccountPagesTranslations : IDataMigration
{
    public string Key => "D_20261015_002";

    public string Description => "Translations of the connected accounts tab";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
