using Auxilia.Application;
using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Localization;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Localization;
using Auxilia.Application.Localization.Public;
using Auxilia.Domain.Localization;
using Auxilia.Domain.Platform;
using Auxilia.Infrastructure;
using Auxilia.Persistence.Tenant;
using Auxilia.Persistence.Tenant.DataMigrations.Localization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace Auxilia.Persistence.Tests.Tenant;

/// <summary>F24 on PostgreSQL: seeds of languages and keys, the reader of the editor, edits through the Manager.</summary>
[Collection(TenantDatabaseGroup.Name)]
public sealed class LocalizationPersistenceTests(TenantDatabaseFixture database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Seeds_AreIdempotentAndKeepCustomisedTranslations()
    {
        await SeedAsync();
        await using (var db = database.CreateContext())
        {
            var edited = await db.Set<ResourceKey>().Include(key => key.Translations).SingleAsync(key => key.Key == "Save", Ct);
            edited.SetTranslation("it", "Memorizza");
            edited.RemoveTranslation("en");
            await db.SaveChangesAsync(Ct);
        }

        await SeedAsync();

        await using var read = database.CreateContext();
        var languages = await read.Set<Language>().AsNoTracking().Select(language => language.Code).ToListAsync(Ct);
        languages.Order().ShouldBe(["en", "it"]);
        var keys = await read.Set<ResourceKey>().AsNoTracking().Include(key => key.Translations).Where(key => key.IsSystem).ToListAsync(Ct);
        keys.Count.ShouldBe(TranslationSeed.LoadAll().Count);
        var save = keys.Single(key => key.Key == "Save");
        save.Translation("it")!.Value.ShouldBe("Memorizza");
        save.Translation("en")!.Value.ShouldBe("Save");
        keys.Single(key => key.Key == "nav.cases").Translation("it")!.Value.ShouldBe("Pratiche");
        keys.Where(key => key.Key != "Save").ShouldAllBe(key => key.Translations.Count == 2 && key.Translations.All(translation => !translation.IsCustomized));
    }

    [Fact]
    public async Task EditorAndBundles_WorkOnTheDatabase()
    {
        await SeedAsync();
        await using var services = Services();
        await using var scope = services.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<ILocalizationQueryService>();
        var keys = scope.ServiceProvider.GetRequiredService<IResourceKeyManager>();
        var marker = "test" + Guid.NewGuid().ToString("N")[..6];

        var id = (await keys.CreateAsync(new CreateResourceKey($"app.{marker}.title", "app", null, new Dictionary<string, string> { ["en"] = "Title 50%" }), Ct)).Value;
        (await keys.SetTranslationAsync(id, "it", "Titolo", Ct)).IsSuccess.ShouldBeTrue();
        (await keys.SetTranslationAsync(id, "it", "Titolo nuovo", Ct)).IsSuccess.ShouldBeTrue();
        (await keys.UpdateAsync(new UpdateResourceKey(id, "common", "Page title"), Ct)).IsSuccess.ShouldBeTrue();

        var key = (await queries.GetKeyAsync(id, Ct)).Value;
        (key.Category, key.Description, key.IsSystem).ShouldBe(("common", "Page title", false));
        key.Translations.Select(translation => (translation.LanguageCode, translation.Value, translation.IsCustomized))
            .ShouldBe([("en", "Title 50%", true), ("it", "Titolo nuovo", true)]);

        (await queries.ListKeysAsync(new ResourceKeyQuery(marker.ToUpperInvariant(), null, null, null, 1, 10), Ct)).Value.TotalCount.ShouldBe(1);
        (await queries.ListKeysAsync(new ResourceKeyQuery("NUOVO", "common", null, null, 1, 10), Ct)).Value.Items.ShouldContain(item => item.Id == id);
        (await queries.ListKeysAsync(new ResourceKeyQuery("50%", null, null, null, 1, 10), Ct)).Value.Items.ShouldAllBe(item => item.Translations.Any(t => t.Value.Contains("50%")));
        var sorted = (await queries.ListKeysAsync(new ResourceKeyQuery(null, null, null, "-category", 1, 5), Ct)).Value;
        sorted.Items.Select(item => item.Category).ShouldBe(sorted.Items.Select(item => item.Category).OrderDescending(StringComparer.Ordinal));
        sorted.TotalCount.ShouldBeGreaterThan(500);

        (await keys.RemoveTranslationAsync(id, "it", Ct)).IsSuccess.ShouldBeTrue();
        (await queries.ListKeysAsync(new ResourceKeyQuery(marker, null, "it", "key", 1, 10), Ct)).Value.Items.ShouldHaveSingleItem().MissingLanguages.ShouldBe(["it"]);

        var bundle = (await queries.GetBundleAsync("it", Ct)).Value;
        bundle.Values["Save"].ShouldNotBeNullOrWhiteSpace();
        bundle.Values[$"app.{marker}.title"].ShouldBe("Title 50%");
        (await scope.ServiceProvider.GetRequiredService<ILocalizer>().GetAsync("nav.cases", "it", null, Ct)).ShouldBe("Pratiche");

        var stats = await queries.ListLanguageStatsAsync(Ct);
        stats.Single(language => language.Code == "en").MissingCount.ShouldBe(0);
        (await queries.ListCategoriesAsync(Ct)).Select(category => category.Category).ShouldContain("nav");

        (await keys.DeleteAsync(id, Ct)).IsSuccess.ShouldBeTrue();
        await using var db = database.CreateContext();
        (await db.Set<ResourceTranslation>().CountAsync(translation => translation.ResourceKeyId == id, Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task KeyNames_AreUnique()
    {
        await using var db = database.CreateContext();
        db.Set<ResourceKey>().Add(ResourceKey.Create(Guid.CreateVersion7(), "dup.key", "common", null, false).Value);
        db.Set<ResourceKey>().Add(ResourceKey.Create(Guid.CreateVersion7(), "dup.key", "common", null, false).Value);

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    private async Task SeedAsync()
    {
        await using var db = database.CreateContext();
        await new D_20260930_003_SeedLegacyTranslations().ApplyAsync(db, Ct);
        await new D_20260930_004_SeedSystemTranslations().ApplyAsync(db, Ct);
        await new D_20260930_005_SeedWebAppTranslations().ApplyAsync(db, Ct);
        await new D_20260930_006_SeedAuthAndShellTranslations().ApplyAsync(db, Ct);
        await new D_20260930_007_SeedUiKitTranslations().ApplyAsync(db, Ct);
        await new D_20260930_008_SeedPlatformConsoleTranslations().ApplyAsync(db, Ct);
        await new D_20260930_009_SeedTenantAdministrationTranslations().ApplyAsync(db, Ct);
        await new D_20261001_001_SeedSettingsAndBrandingTranslations().ApplyAsync(db, Ct);
        await new D_20261001_002_SeedMessagingConsoleTranslations().ApplyAsync(db, Ct);
        await new D_20261001_003_SeedCustomizationTranslations().ApplyAsync(db, Ct);
        await new D_20261001_004_SeedLocalizationEditorTranslations().ApplyAsync(db, Ct);
        await new D_20261001_005_SeedAccessTranslations().ApplyAsync(db, Ct);
        await new D_20261002_001_SeedLogPageTranslations().ApplyAsync(db, Ct);
        await new D_20261002_002_SeedClientTranslations().ApplyAsync(db, Ct);
        await new D_20261002_003_SeedEmployeeTranslations().ApplyAsync(db, Ct);
        await new D_20261003_001_SeedProfileTranslations().ApplyAsync(db, Ct);
        await new D_20261003_002_SeedClientPagesTranslations().ApplyAsync(db, Ct);
        await new D_20261003_003_SeedEmployeeAndProfilePagesTranslations().ApplyAsync(db, Ct);
        await new D_20261003_004_SeedRegistrationTranslations().ApplyAsync(db, Ct);
        await new D_20261003_005_SeedServiceCatalogTranslations().ApplyAsync(db, Ct);
        await new D_20261003_006_SeedCaseTranslations().ApplyAsync(db, Ct);
        await new D_20261003_007_SeedCaseListTranslations().ApplyAsync(db, Ct);
        await new D_20261003_008_SeedServiceFolderTranslations().ApplyAsync(db, Ct);
        await new D_20261003_009_SeedFileStorageTranslations().ApplyAsync(db, Ct);
        await new D_20261003_010_SeedDocumentTranslations().ApplyAsync(db, Ct);
        await new D_20261003_011_SeedDocumentPagesTranslations().ApplyAsync(db, Ct);
        await new D_20261003_012_SeedCasePagesTranslations().ApplyAsync(db, Ct);
        await new D_20261003_013_SeedServicePagesTranslations().ApplyAsync(db, Ct);
        await new D_20261003_014_SeedAppointmentTranslations().ApplyAsync(db, Ct);
        await new D_20261003_015_SeedAppointmentPagesTranslations().ApplyAsync(db, Ct);
        await new D_20261003_016_SeedRequestTranslations().ApplyAsync(db, Ct);
        await new D_20261004_002_SeedNotificationTranslations().ApplyAsync(db, Ct);
        await new D_20261004_003_SeedRequestNotificationSessionPagesTranslations().ApplyAsync(db, Ct);
        await new D_20261004_004_SeedLoginAuditRangeTranslations().ApplyAsync(db, Ct);
        await new D_20261004_005_SeedExportTranslations().ApplyAsync(db, Ct);
        await new D_20261004_006_SeedDashboardTranslations().ApplyAsync(db, Ct);
        await new D_20261004_007_SeedDashboardPagesTranslations().ApplyAsync(db, Ct);
        await new D_20261004_008_SeedCaseExpiryTranslations().ApplyAsync(db, Ct);
        await new D_20261004_009_SeedImportTranslations().ApplyAsync(db, Ct);
        await new D_20261004_010_SeedCrmTranslations().ApplyAsync(db, Ct);
        await new D_20261004_011_SeedConsentTagTranslations().ApplyAsync(db, Ct);
        await new D_20261004_012_SeedAudienceTranslations().ApplyAsync(db, Ct);
        await new D_20261005_001_SeedCampaignTranslations().ApplyAsync(db, Ct);
    }

    private ServiceProvider Services()
    {
        var tenant = new TenantInfo(Guid.Parse("0199a0b2-0000-7000-8000-0000000001d1"), "tenant-test", TenantStatus.Active, "it", "Europe/Rome");
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.Current.Returns(tenant);
        tenantContext.Tenant.Returns(tenant);
        var directory = Substitute.For<ITenantDirectory>();
        directory.GetProtectedConnectionStringAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns("protected");
        var protector = Substitute.For<ITenantConnectionProtector>();
        protector.Unprotect("protected").Returns(database.ConnectionString);
        var user = Substitute.For<ICurrentUser>();
        user.ActorType.Returns(ActorType.System);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure();
        services.AddScoped(_ => tenantContext);
        services.AddScoped(_ => directory);
        services.AddSingleton(protector);
        services.AddScoped(_ => user);
        services.AddScoped(_ => Substitute.For<IPlatformSettingStore>());
        services.AddSingleton(Substitute.For<ISettingSecretProtector>());
        services.AddApplication();
        services.AddTenantPersistence();
        return services.BuildServiceProvider(validateScopes: true);
    }
}
