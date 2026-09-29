using System.Text.Json;

namespace Auxilia.Architecture.Tests;

/// <summary>
/// Blocklisted packages (auxilia-dependency-policy) must not appear directly or transitively.
/// Reads every packages.lock.json, which contains the resolved transitive graph.
/// </summary>
public sealed class BlocklistedPackageTests
{
    private static readonly string[] BlockedPrefixes =
    [
        "MediatR", "AutoMapper", "FluentAssertions", "MassTransit", "EPPlus", "QuestPDF", "Moq",
        "Duende.", "SixLabors.ImageSharp", "Telerik.", "Syncfusion.", "DevExpress.", "Kendo.",
        "Hangfire.Pro", "AG-Grid",

        // Open Source Maintenance Fee EULA (json-everything) and Aspire hosting, which depends on it (ADR 0013).
        "JsonPatch.Net", "JsonPointer.Net", "Json.More.Net", "JsonSchema.Net", "Aspire.Hosting", "Aspire.AppHost",
    ];

    [Fact]
    public void LockFiles_ExistForEveryProject()
    {
        var projects = Directory.GetFiles(Solution.BackendDirectory.FullName, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        foreach (var project in projects)
        {
            File.Exists(Path.Combine(Path.GetDirectoryName(project)!, "packages.lock.json"))
                .ShouldBeTrue($"missing packages.lock.json for {Path.GetFileName(project)}");
        }
    }

    [Fact]
    public void ResolvedPackages_ContainNoBlocklistedPackage()
    {
        var violations = LockFiles()
            .SelectMany(file => ResolvedPackageIds(file).Select(id => (File: file, Id: id)))
            .Where(package => BlockedPrefixes.Any(prefix => package.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Select(package => $"{package.Id} in {Path.GetRelativePath(Solution.BackendDirectory.FullName, package.File)}")
            .Distinct()
            .ToArray();

        violations.ShouldBeEmpty();
    }

    private static string[] LockFiles() =>
        Directory.GetFiles(Solution.BackendDirectory.FullName, "packages.lock.json", SearchOption.AllDirectories);

    private static IEnumerable<string> ResolvedPackageIds(string lockFile)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(lockFile));
        foreach (var framework in document.RootElement.GetProperty("dependencies").EnumerateObject())
        {
            foreach (var package in framework.Value.EnumerateObject())
            {
                yield return package.Name;
            }
        }
    }
}
