using Auxilia.Application.Abstractions.Modules;

namespace Auxilia.Application.Platform.Modules;

/// <inheritdoc cref="IModuleRegistry"/>
internal sealed class ModuleRegistry : IModuleRegistry
{
    private readonly Dictionary<string, IModuleDescriptor> modules;

    public ModuleRegistry(IEnumerable<IModuleDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        modules = new Dictionary<string, IModuleDescriptor>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            if (!ModuleCodes.IsValid(descriptor.Code))
            {
                throw new InvalidOperationException($"Module code '{descriptor.Code}' must be lower-case letters (at most {ModuleCodes.MaxLength}).");
            }

            if (!modules.TryAdd(descriptor.Code, descriptor) && !ReferenceEquals(modules[descriptor.Code], descriptor))
            {
                throw new InvalidOperationException($"Module '{descriptor.Code}' is registered twice.");
            }
        }

        var sharedRange = modules.Values.GroupBy(module => module.EventCodeRangeStart).FirstOrDefault(group => group.Count() > 1);
        if (sharedRange is not null)
        {
            throw new InvalidOperationException($"Modules {string.Join(", ", sharedRange.Select(module => module.Code))} share event code range {sharedRange.Key}.");
        }

        All = modules.Values.OrderBy(module => module.Code, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<IModuleDescriptor> All { get; }

    public IModuleDescriptor? Find(string code) => modules.GetValueOrDefault(code);
}

internal static class ModuleCodes
{
    public const int MaxLength = Domain.Platform.PlatformModule.CodeMaxLength;

    public static bool IsValid(string code) =>
        !string.IsNullOrEmpty(code) && code.Length <= MaxLength && code.All(character => character is >= 'a' and <= 'z');
}
