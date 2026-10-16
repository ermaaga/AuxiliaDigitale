namespace Auxilia.Persistence.Tenant.DataMigrations.Localization;

/// <summary>Texts of the mail page (N05, C-07): menu, folders, list, reading pane and "file into case". EN and IT, idempotent.</summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261016_005_SeedMailPageTranslations : IDataMigration
{
    public string Key => "D_20261016_005";

    public string Description => "Translations of the mail page";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        await TranslationSeed.EnsureLanguagesAsync(db, cancellationToken);
        await TranslationSeed.UpsertAsync(db, TranslationSeed.Load(Key), cancellationToken);
    }
}
