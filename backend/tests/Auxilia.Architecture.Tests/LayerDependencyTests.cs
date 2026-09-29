using NetArchTest.Rules;

namespace Auxilia.Architecture.Tests;

/// <summary>Type-level dependency rules (NetArchTest); they become meaningful as types are added.</summary>
public sealed class LayerDependencyTests
{
    private static readonly string[] InfrastructureNamespaces =
    [
        "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Rebus", "StackExchange.Redis", "Npgsql",
        "Azure.Storage", "MailKit", "FluentFTP", "Serilog",
    ];

    [Theory]
    [InlineData(Solution.SharedKernel)]
    [InlineData(Solution.Domain)]
    [InlineData(Solution.Contracts)]
    public void CoreLayers_DoNotDependOnInfrastructureLibraries(string assembly)
    {
        var result = Types.InAssembly(Solution.Load(assembly))
            .ShouldNot().HaveDependencyOnAny(InfrastructureNamespaces)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Application_DoesNotDependOnOuterLayersOrInfrastructureLibraries()
    {
        var result = Types.InAssembly(Solution.Load(Solution.Application))
            .ShouldNot().HaveDependencyOnAny(
            [
                Solution.Infrastructure, Solution.PersistenceCatalog, Solution.PersistenceTenant,
                Solution.Api, Solution.Worker, Solution.MigrationRunner,
                "Microsoft.EntityFrameworkCore.Relational", "Npgsql", "Rebus", "StackExchange.Redis",
            ])
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Theory]
    [InlineData(Solution.Infrastructure)]
    [InlineData(Solution.PersistenceCatalog)]
    [InlineData(Solution.PersistenceTenant)]
    public void InnerAdapters_DoNotDependOnHosts(string assembly)
    {
        var result = Types.InAssembly(Solution.Load(assembly))
            .ShouldNot().HaveDependencyOnAny([Solution.Api, Solution.Worker, Solution.MigrationRunner])
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    internal static string Describe(NetArchTest.Rules.TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : "Violating types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
