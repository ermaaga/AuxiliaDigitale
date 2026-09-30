using Auxilia.Application.Abstractions.Settings;

namespace Auxilia.Application.Configuration;

/// <summary>The definitions registered in DI (<see cref="DependencyInjection.AddSettingDefinitions"/>); duplicate keys fail at startup.</summary>
internal sealed class SettingDefinitionRegistry : ISettingDefinitionRegistry
{
    private readonly Dictionary<string, SettingDefinition> definitions;

    public SettingDefinitionRegistry(IEnumerable<SettingDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        this.definitions = new Dictionary<string, SettingDefinition>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            if (!this.definitions.TryAdd(definition.Key, definition) && !ReferenceEquals(this.definitions[definition.Key], definition))
            {
                throw new InvalidOperationException($"Setting '{definition.Key}' is defined twice.");
            }
        }

        All = this.definitions.Values.OrderBy(definition => definition.Key, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyCollection<SettingDefinition> All { get; }

    public SettingDefinition? Find(string key) => definitions.GetValueOrDefault(key);
}
