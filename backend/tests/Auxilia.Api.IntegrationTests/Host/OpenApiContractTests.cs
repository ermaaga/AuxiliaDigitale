using System.Text.Json;
using System.Text.Json.Nodes;

namespace Auxilia.Api.IntegrationTests.Host;

/// <summary>
/// The committed contract <c>backend/openapi/v1.json</c> equals the document the API serves (skill auxilia-api-contract).
/// Regenerate with <c>AUXILIA_UPDATE_CONTRACTS=1 dotnet test --project tests/Auxilia.Api.IntegrationTests</c>, then
/// <c>pnpm --filter @auxilia/api-client generate</c> in <c>frontend/</c>.
/// </summary>
public sealed class OpenApiContractTests : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly ApiFactory factory;

    public OpenApiContractTests(ApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task CommittedContract_MatchesServedDocument()
    {
        using var client = factory.CreateClient();
        var served = JsonNode.Parse(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken))!
            .ToJsonString(Indented).ReplaceLineEndings("\n") + "\n";
        var path = Path.Combine(BackendDirectory(), "openapi", "v1.json");

        if (Environment.GetEnvironmentVariable("AUXILIA_UPDATE_CONTRACTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, served, TestContext.Current.CancellationToken);
        }

        File.Exists(path).ShouldBeTrue("backend/openapi/v1.json is missing");
        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n").ShouldBe(
            served,
            "contract out of date: run AUXILIA_UPDATE_CONTRACTS=1 dotnet test --project tests/Auxilia.Api.IntegrationTests, then regenerate the api-client");
    }

    private static string BackendDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Auxilia.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Auxilia.slnx not found above " + AppContext.BaseDirectory);
    }
}
