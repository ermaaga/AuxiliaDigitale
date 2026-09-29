using System.Reflection;

namespace Auxilia.Architecture.Tests;

/// <summary>Locates the solution on disk and loads the production assemblies under test.</summary>
internal static class Solution
{
    public const string SharedKernel = "Auxilia.SharedKernel";
    public const string Diagnostics = "Auxilia.Diagnostics";
    public const string Domain = "Auxilia.Domain";
    public const string Contracts = "Auxilia.Contracts";
    public const string Application = "Auxilia.Application";
    public const string Infrastructure = "Auxilia.Infrastructure";
    public const string PersistenceCatalog = "Auxilia.Persistence.Catalog";
    public const string PersistenceTenant = "Auxilia.Persistence.Tenant";
    public const string ServiceDefaults = "Auxilia.ServiceDefaults";
    public const string Api = "Auxilia.Api";
    public const string Worker = "Auxilia.Worker";
    public const string MigrationRunner = "Auxilia.MigrationRunner";

    public static readonly string[] ProductionProjects =
    [
        SharedKernel, Diagnostics, Domain, Contracts, Application, Infrastructure,
        PersistenceCatalog, PersistenceTenant, ServiceDefaults, Api, Worker, MigrationRunner,
    ];

    /// <summary>Modules (bounded contexts) — docs/architecture/ARCHITECTURE.md §1.</summary>
    public static readonly string[] Modules =
    [
        "Platform", "Tenancy", "Identity", "Directory", "Cases", "Documents", "Scheduling", "Engagement",
        "Messaging", "Marketing", "Imports", "Configuration", "Localization", "Reporting", "Audit",
    ];

    public static DirectoryInfo BackendDirectory { get; } = FindBackendDirectory();

    public static Assembly Load(string assemblyName) => Assembly.Load(assemblyName);

    public static string ProjectFile(string projectName)
    {
        var folder = projectName.EndsWith("Tests", StringComparison.Ordinal) || projectName == "Auxilia.Tests.Common"
            ? "tests"
            : "src";
        return Path.Combine(BackendDirectory.FullName, folder, projectName, projectName + ".csproj");
    }

    private static DirectoryInfo FindBackendDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Auxilia.slnx")))
            {
                return directory;
            }
        }

        throw new InvalidOperationException("Auxilia.slnx not found above " + AppContext.BaseDirectory);
    }
}
