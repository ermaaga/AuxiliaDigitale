using Auxilia.Application.Abstractions.Modules;
using Auxilia.Domain.Configuration;
using Auxilia.Infrastructure.Adapters.Channels.Smtp;
using Auxilia.MigrationRunner.LegacyImport.Steps;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.LegacyImport.Tests;

/// <summary>The configuration rules of E-05 (docs/migration/mapping.md §5.5, §6), without databases.</summary>
public sealed class ConfigurationMappingTests
{
    [Theory]
    [InlineData("linear-gradient(135deg, #667eea 0%, #764ba2 100%)", "#667eea", "#764ba2")]
    [InlineData("#abc, #def", "#abc", "#def")]
    public void GradientColors_ReadTheTwoColours(string css, string start, string end) =>
        SettingsStep.GradientColors(css).ShouldBe((start, end));

    [Fact]
    public void GradientColors_WithoutTwoColours_IsNull() => SettingsStep.GradientColors("red").ShouldBeNull();

    [Theory]
    [InlineData(true, 587, SmtpSecurity.StartTls)]
    [InlineData(true, 465, SmtpSecurity.SslOnConnect)]
    [InlineData(false, 25, SmtpSecurity.None)]
    public void Security_FollowsSslAndPort(bool ssl, int port, SmtpSecurity expected) => MessagingAccountStep.Security(ssl, port).ShouldBe(expected);

    [Fact]
    public void SmtpProvider_IsTheAdapterKey() => MessagingAccountStep.SmtpProvider.ShouldBe("smtp");

    [Theory]
    [InlineData("User", "client")]
    [InlineData("Subscription", "case")]
    [InlineData("WorkoutPlan", null)]
    public void CustomFieldEntity_MapsTheLegacyEntity(string legacy, string? expected) => CustomFieldsStep.Entity(legacy).ShouldBe(expected);

    [Theory]
    [InlineData("boolean", CustomFieldType.Boolean)]
    [InlineData("Number", CustomFieldType.Number)]
    [InlineData("datetime", CustomFieldType.Date)]
    [InlineData("text", CustomFieldType.Text)]
    [InlineData(null, CustomFieldType.Text)]
    public void CustomFieldType_MapsTheLegacyType(string? legacy, CustomFieldType expected) => CustomFieldsStep.Type(legacy).ShouldBe(expected);

    [Theory]
    [InlineData("Common", "common")]
    [InlineData("with space", "legacy")]
    [InlineData("", "legacy")]
    public void Category_FollowsTheNewRule(string legacy, string expected) => LocalizationStep.Category(legacy).ShouldBe(expected);

    [Fact]
    public void PagePermissions_AreDeclaredByTheModules()
    {
        var declared = ImportHarness.Modules.Value.All.SelectMany(module => module.Permissions).Select(permission => permission.Code).ToHashSet(StringComparer.Ordinal);

        AccessStep.PagePermissions.Values.SelectMany(codes => codes).Where(code => !declared.Contains(code)).ShouldBeEmpty();
    }

    [Fact]
    public void PageGrids_AreDeclaredByTheModules()
    {
        var grids = ImportHarness.Modules.Value.Grids;

        foreach (var (grid, columns) in AccessStep.PageGrids.Values)
        {
            grids.ShouldContainKey(grid);
            columns.Values.Where(key => grids[grid].Columns.All(column => column.Key != key)).ShouldBeEmpty(grid);
        }
    }

    [Fact]
    public void Layout_ShowsTheLegacyColumnsInOrder_AndHidesTheOthers()
    {
        var grid = new GridDefinition("g", [new("a", "a"), new("b", "b"), new("c", "c", CanHide: false), new("d", "d"), new("e", "e", VisibleByDefault: false)], [TenantRole.Administrator]);
        var columns = new Dictionary<string, string> { ["B"] = "b", ["A"] = "a" };

        // a: the legacy grid could show it but did not; c: never hidden; d, e: new columns keep their default.
        AccessStep.Layout(grid, columns, ["B", "Unknown"]).ShouldBe([
            new GridLayoutColumn("b", true), new GridLayoutColumn("a", false), new GridLayoutColumn("c", true), new GridLayoutColumn("d", true), new GridLayoutColumn("e", false)]);
    }
}
