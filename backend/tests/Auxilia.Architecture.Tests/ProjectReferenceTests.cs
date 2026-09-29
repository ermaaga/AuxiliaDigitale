using System.Xml.Linq;

namespace Auxilia.Architecture.Tests;

/// <summary>
/// Dependency direction between projects (ADR 0004, auxilia-architecture "Solution layout").
/// Checks the declared ProjectReferences, so it guards the layering even before any type exists.
/// </summary>
public sealed class ProjectReferenceTests
{
    private static readonly Dictionary<string, string[]> AllowedReferences = new()
    {
        [Solution.SharedKernel] = [],
        [Solution.Contracts] = [],
        [Solution.ServiceDefaults] = [],
        [Solution.Diagnostics] = [Solution.SharedKernel],
        [Solution.Domain] = [Solution.SharedKernel, Solution.Diagnostics],
        [Solution.Application] = [Solution.SharedKernel, Solution.Diagnostics, Solution.Domain, Solution.Contracts],
        [Solution.Infrastructure] = [Solution.Application, Solution.Domain],
        [Solution.PersistenceCatalog] = [Solution.Application, Solution.Domain],
        [Solution.PersistenceTenant] = [Solution.Application, Solution.Domain],
        [Solution.Api] =
        [
            Solution.Application, Solution.Contracts, Solution.Infrastructure,
            Solution.PersistenceCatalog, Solution.PersistenceTenant, Solution.ServiceDefaults,
        ],
        [Solution.Worker] =
        [
            Solution.Application, Solution.Infrastructure,
            Solution.PersistenceCatalog, Solution.PersistenceTenant, Solution.ServiceDefaults,
        ],
        [Solution.MigrationRunner] = [Solution.Infrastructure, Solution.PersistenceCatalog, Solution.PersistenceTenant],
    };

    public static TheoryData<string> Projects() => new(Solution.ProductionProjects);

    [Fact]
    public void Rules_CoverEveryProductionProject()
    {
        AllowedReferences.Keys.ShouldBe(Solution.ProductionProjects, ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void ProjectReferences_OnlyPointInwards(string project)
    {
        var referenced = ReadProjectReferences(project);

        var forbidden = referenced.Except(AllowedReferences[project]).ToArray();

        forbidden.ShouldBeEmpty($"{project} must not reference {string.Join(", ", forbidden)}");
    }

    [Theory]
    [InlineData(Solution.SharedKernel)]
    [InlineData(Solution.Contracts)]
    [InlineData(Solution.Domain)]
    public void CoreProjects_HaveNoPackageReferences(string project)
    {
        ReadElements(project, "PackageReference").ShouldBeEmpty($"{project} must not reference NuGet packages");
    }

    [Fact]
    public void Diagnostics_ReferencesOnlyLoggingAbstractions()
    {
        ReadElements(Solution.Diagnostics, "PackageReference").ShouldBe(["Microsoft.Extensions.Logging.Abstractions"]);
    }

    private static string[] ReadProjectReferences(string project) =>
        ReadElements(project, "ProjectReference")
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
            .ToArray();

    private static string[] ReadElements(string project, string elementName) =>
        XDocument.Load(Solution.ProjectFile(project))
            .Descendants(elementName)
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
            .ToArray();
}
