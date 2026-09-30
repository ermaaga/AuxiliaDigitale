using Auxilia.Application;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Diagnostics;

namespace Auxilia.Architecture.Tests;

/// <summary>Module descriptors (ARCHITECTURE §5.1): all registered, bound to an event-code range, consistent navigation.</summary>
public sealed class ModuleDescriptorTests
{
    private static IReadOnlyList<IModuleDescriptor> Modules => DependencyInjection.Modules;

    [Fact]
    public void EveryDescriptorInTheApplication_IsRegistered()
    {
        var declared = Solution.Load(Solution.Application).GetTypes()
            .Where(type => typeof(IModuleDescriptor).IsAssignableFrom(type) && type is { IsClass: true, IsAbstract: false })
            .ToArray();

        declared.ShouldBe(Modules.Select(module => module.GetType()), ignoreOrder: true);
    }

    [Fact]
    public void Descriptor_LivesInItsModuleNamespaceAndStartsAnEventCodeRange()
    {
        var ranges = EventRegistry.Ranges().Select(range => range.Start).ToHashSet();

        foreach (var module in Modules)
        {
            ranges.ShouldContain(module.EventCodeRangeStart, $"module {module.Code}");
            module.GetType().Namespace.ShouldBe($"{Solution.Application}.{module.GetType().Name[..^"Module".Length]}");
            module.GetType().Name.ToLowerInvariant().ShouldBe(module.Code + "module");
        }

        Modules.Select(module => module.EventCodeRangeStart).ShouldBeUnique();
    }

    [Fact]
    public void Navigation_HasUniqueKeysRelativeRoutesAndRoles()
    {
        var entries = Modules.SelectMany(module => module.Navigation).ToArray();

        entries.Select(entry => entry.Key).ShouldBeUnique();
        entries.ShouldAllBe(entry => entry.Route.StartsWith('/') && entry.Roles.Count > 0 && entry.Icon.Length > 0);
    }

    [Fact]
    public void Settings_BelongToTheModuleThatDeclaresThem()
    {
        foreach (var module in Modules)
        {
            var owner = module.GetType().Name[..^"Module".Length];
            module.Settings.Select(setting => setting.Module).ShouldAllBe(name => name == owner);
        }
    }
}
