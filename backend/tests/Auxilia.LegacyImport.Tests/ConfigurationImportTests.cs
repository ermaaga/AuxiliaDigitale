using System.Text.Json;

using Auxilia.Domain.Configuration;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Localization;
using Auxilia.Domain.Messaging;
using Auxilia.MigrationRunner.LegacyImport;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.LegacyImport.Tests;

/// <summary>E-05 end to end: translations, settings and branding, the SMTP account, permissions, grids and custom fields.</summary>
[Collection(LegacyDatabaseGroup.Name)]
public sealed class ConfigurationImportTests(LegacyDatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Import_Configuration_FollowsTheMapping_AndRepeats()
    {
        var connection = await fixture.CreateLegacyAsync("legacy_configuration", "legacy-develop.sql", LegacyDatabaseFixture.Script("configuration.sql"));
        await using var source = (await LegacySource.OpenAsync(connection, Ct)).Value;
        await using var tenant = await fixture.CreateTenantAsync("tenant_configuration");
        await using (var db = ImportHarness.Context(tenant))
        {
            // What a migrated tenant has before the import: the seed of the shipped texts and the default grants.
            db.Add(new Language(Guid.CreateVersion7(), "it", "Italiano"));
            db.Add(new Language(Guid.CreateVersion7(), "en", "English"));
            var save = ResourceKey.Create(Guid.CreateVersion7(), "Save", "common", null, isSystem: true).Value;
            save.UpgradeSystemTranslation("it", "Salva");
            save.UpgradeSystemTranslation("en", "Save");
            db.Add(save);
            db.Add(new PermissionEntry("engagement.requests.view", "engagement"));
            db.Add(new PermissionEntry("engagement.requests.manage", "engagement"));
            db.Add(new RoleGrant(TenantRole.Employee, "engagement.requests.view"));
            db.Add(new RoleGrant(TenantRole.Employee, "engagement.requests.manage"));
            db.Add(new RoleGrant(TenantRole.Client, "engagement.requests.view"));
            await db.SaveChangesAsync(Ct);
        }

        var report = await ImportHarness.ImportAsync(source, tenant);

        report.Issues.ShouldContain(issue => issue.Table == "SystemConfigurations" && issue.Reason == "unknown setting Mystery: left out");
        report.Issues.ShouldContain(issue => issue.Table == "SystemConfigurations" && issue.Reason == "value of AutoSubscriptionExpiry not valid: default kept");
        report.Issues.ShouldContain(issue => issue.Table == "PageConfigurations" && issue.Reason == "page Mystery has no equivalent permissions: left out");
        report.Issues.ShouldContain(issue => issue.Table == "EntityConfigurations" && issue.Reason == "a field name is not a valid key: left out");
        report.Tables["ResourceTranslations"].Excluded.ShouldBe(1);
        report.Tables["EntityConfigurations"].Skipped.ShouldBe(1);

        await using (var db = ImportHarness.Context(tenant))
        {
            var keys = await db.Set<ResourceKey>().Include(key => key.Translations).ToDictionaryAsync(key => key.Key, Ct);
            keys["Save"].Translation("it")!.Value.ShouldBe("Salva ora");
            keys["Save"].Translation("it")!.IsCustomized.ShouldBeTrue();
            keys["Save"].Translation("en")!.IsCustomized.ShouldBeFalse();
            (keys["TenantOnly"].IsSystem, keys["TenantOnly"].Category, keys["TenantOnly"].Translation("it")!.Value).ShouldBe((false, "custom", "Solo nostro"));
            keys.ContainsKey("WorkoutPlans").ShouldBeFalse();

            var settings = await db.Set<TenantSetting>().ToDictionaryAsync(setting => setting.Key, setting => setting.JsonValue, Ct);
            settings["registration.enabled"].ShouldBe("true");
            settings["cases.expiry.expiringDays"].ShouldBe("15");
            settings.ContainsKey("cases.expiry.enabled").ShouldBeFalse();
            settings["branding.useAppName"].ShouldBe("false");
            settings["branding.theme.fill"].ShouldBe("\"Gradient\"");
            (settings["branding.theme.primaryColor"], settings["branding.theme.accentColor"]).ShouldBe(("\"#112233\"", "\"#445566\""));
            settings["branding.background.kind"].ShouldBe("\"Image\"");
            (settings["branding.background.startColor"], settings["branding.background.endColor"]).ShouldBe(("\"#abcdef\"", "\"#fedcba\""));
            (await db.Set<BrandingAsset>().SingleAsync(Ct)).ContentType.ShouldBe("image/jpeg");

            var account = await db.Set<MessagingAccount>().SingleAsync(Ct);
            (account.Channel, account.Provider, account.IsDefault, account.IsActive, account.SecretProtected).ShouldBe((MessageChannel.Email, "smtp", true, true, "protected:not-a-real-secret"));
            using var smtp = JsonDocument.Parse(account.SettingsJson);
            (smtp.RootElement.GetProperty("host").GetString(), smtp.RootElement.GetProperty("port").GetInt32(), smtp.RootElement.GetProperty("security").GetString())
                .ShouldBe(("smtp.example.test", 587, "StartTls"));

            (await db.Set<RoleGrant>().Select(grant => new { grant.Role, grant.PermissionCode }).ToListAsync(Ct))
                .ShouldBe([new { Role = TenantRole.Client, PermissionCode = "engagement.requests.view" }]);

            var layout = await db.Set<GridLayout>().SingleAsync(Ct);
            (layout.GridKey, layout.Role).ShouldBe(("cases.cases", "Administrator"));
            layout.Columns.Where(column => column.Visible).Select(column => column.Key).ShouldBe(["service", "client", "number"]);
            layout.Columns.Single(column => column.Key == "amountPaid").Visible.ShouldBeFalse();

            var fields = await db.Set<CustomFieldDefinition>().OrderBy(field => field.Order).ToListAsync(Ct);
            fields.Select(field => (field.EntityType, field.Key, field.Type, field.VisibleOnGrid, field.DashboardCounter))
                .ShouldBe([("client", "CAF", CustomFieldType.Boolean, true, true), ("client", "Note", CustomFieldType.Text, false, false)]);
        }

        await ImportHarness.ImportAsync(source, tenant);

        await using (var db = ImportHarness.Context(tenant))
        {
            (await db.Set<MessagingAccount>().CountAsync(Ct)).ShouldBe(1);
            (await db.Set<CustomFieldDefinition>().CountAsync(Ct)).ShouldBe(2);
            (await db.Set<GridLayout>().CountAsync(Ct)).ShouldBe(1);
            (await db.Set<BrandingAsset>().CountAsync(Ct)).ShouldBe(1);
        }
    }
}
