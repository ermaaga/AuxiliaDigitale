using NetArchTest.Rules;

namespace Auxilia.Architecture.Tests;

/// <summary>
/// A module may use another module only through its Public API namespace (`Auxilia.Application.&lt;Module&gt;.Public`)
/// or through contracts/messages (ADR 0004, auxilia-architecture "Module boundaries").
/// </summary>
public sealed class ModuleBoundaryTests
{
    public static TheoryData<string, string> ModulePairs()
    {
        var data = new TheoryData<string, string>();
        foreach (var from in Solution.Modules)
        {
            foreach (var to in Solution.Modules.Where(module => module != from))
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ModulePairs))]
    public void Modules_UseOtherModulesOnlyThroughPublicApi(string from, string to)
    {
        foreach (var layer in new[] { Solution.Domain, Solution.Application })
        {
            var internalNamespaces = Types.InAssembly(Solution.Load(layer))
                .That().ResideInNamespace($"{layer}.{to}")
                .And().DoNotResideInNamespace($"{layer}.{to}.Public")
                .GetTypes()
                .Select(type => type.FullName!)
                .ToArray();

            if (internalNamespaces.Length == 0)
            {
                continue;
            }

            var result = Types.InAssembly(Solution.Load(layer))
                .That().ResideInNamespace($"{layer}.{from}")
                .ShouldNot().HaveDependencyOnAny(internalNamespaces)
                .GetResult();

            result.IsSuccessful.ShouldBeTrue(
                $"{layer}.{from} uses internals of {to}. " + LayerDependencyTests.Describe(result));
        }
    }
}
