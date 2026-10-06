using Auxilia.MigrationRunner.Cli;

using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.LegacyImport.Tests;

/// <summary><c>auxctl legacy inspect</c> on a legacy database (the connection string comes from the environment).</summary>
[Collection(LegacyDatabaseGroup.Name)]
public sealed class LegacySourceCliTests(LegacyDatabaseFixture fixture)
{
    [Fact]
    public async Task LegacyInspect_PrintsTheReport()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var connection = fixture.ConnectionString(LegacyDatabaseFixture.Develop);
        // Without --tenant the command needs only logging from the host.
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var cli = new AuxctlCli(
            () => services, output, error,
            environment: name => name == "AUXILIA_LEGACY_CONNECTION" ? connection : null);

        (await cli.RunAsync(["legacy", "inspect"], TestContext.Current.CancellationToken)).ShouldBe(AuxctlCli.Success, error.ToString());

        output.ToString().ShouldContain("last migration 20260602145759_AddMembershipFolderTemplate");
        output.ToString().ShouldNotContain("Password=", Case.Insensitive);
    }
}
