using System.Text.RegularExpressions;

using Auxilia.MigrationRunner.Cli;
using Auxilia.MigrationRunner.LegacyImport;

namespace Auxilia.LegacyImport.Tests;

/// <summary>The table catalog, the mapping document and the legacy scripts stay in step (no Docker).</summary>
public sealed partial class LegacyCatalogTests
{
    [Theory]
    [InlineData("legacy-develop.sql", false)]
    [InlineData("legacy-security-update.sql", true)]
    public void Catalog_HasEveryTableOfTheBaseline(string script, bool securityUpdate)
    {
        var created = CreatedTables(LegacyDatabaseFixture.Script(script)).Where(table => table != "__EFMigrationsHistory").Order(StringComparer.Ordinal);

        var expected = LegacyTables.All
            .Where(table => !table.Optional && (securityUpdate || !table.SecurityUpdate))
            .Select(table => table.Name)
            .Order(StringComparer.Ordinal);
        created.ShouldBe(expected);
    }

    [Fact]
    public void MappingDocument_HasARowPerLegacyTable()
    {
        var document = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "migration", "mapping.md"));

        LegacyTables.All.Where(table => !document.Contains($"| `{table.Name}` |", StringComparison.Ordinal))
            .Select(table => table.Name)
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task LegacyInspect_WithoutTheConnectionVariable_IsAUsageError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var cli = new AuxctlCli(() => throw new InvalidOperationException("no host needed"), output, error, environment: _ => null);

        (await cli.RunAsync(["legacy", "inspect"], TestContext.Current.CancellationToken)).ShouldBe(AuxctlCli.UsageError);
        error.ToString().ShouldContain(LegacySource.ConnectionVariable);
    }

    private static IEnumerable<string> CreatedTables(string sql) =>
        CreateTable().Matches(sql).Select(match => match.Groups[1].Value);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "docs", "migration")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }

    [GeneratedRegex("""CREATE TABLE (?:IF NOT EXISTS )?"(\w+)" """)]
    private static partial Regex CreateTable();
}
