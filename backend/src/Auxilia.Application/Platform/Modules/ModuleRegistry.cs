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
        PermissionModules = BuildPermissions(All);
        Grids = BuildGrids(All);
        CustomFieldEntities = BuildCustomFieldEntities(All);
    }

    public IReadOnlyList<IModuleDescriptor> All { get; }

    public IReadOnlyDictionary<string, string> PermissionModules { get; }

    public IModuleDescriptor? Find(string code) => modules.GetValueOrDefault(code);

    public IReadOnlyDictionary<string, GridDefinition> Grids { get; }

    public IReadOnlyDictionary<string, string> CustomFieldEntities { get; }

    /// <summary>Grid keys are <c>&lt;module&gt;.&lt;grid&gt;</c> and unique; column keys unique; at least one column always shown.</summary>
    private static Dictionary<string, GridDefinition> BuildGrids(IEnumerable<IModuleDescriptor> descriptors)
    {
        var grids = new Dictionary<string, GridDefinition>(StringComparer.Ordinal);
        foreach (var module in descriptors)
        {
            foreach (var grid in module.Grids)
            {
                var parts = grid.Key.Split('.');
                if (parts.Length != 2 || parts[0] != module.Code || !Identifiers.IsLowerCamel(parts[1]) || grid.Key.Length > Domain.Configuration.GridLayout.GridKeyMaxLength)
                {
                    throw new InvalidOperationException($"Grid '{grid.Key}' of module '{module.Code}' must be '{module.Code}.<grid>' in lower camel case.");
                }

                if (grid.Columns.Count == 0
                    || grid.Columns.Select(column => column.Key).Distinct(StringComparer.Ordinal).Count() != grid.Columns.Count
                    || grid.Columns.Any(column => !Identifiers.IsLowerCamel(column.Key))
                    || grid.Columns.All(column => column.CanHide)
                    || grid.Roles.Count == 0)
                {
                    throw new InvalidOperationException($"Grid '{grid.Key}' needs unique lower camel column keys, one column that cannot be hidden and at least one role.");
                }

                if (!grids.TryAdd(grid.Key, grid))
                {
                    throw new InvalidOperationException($"Grid '{grid.Key}' is declared twice.");
                }
            }
        }

        return grids;
    }

    private static Dictionary<string, string> BuildCustomFieldEntities(IEnumerable<IModuleDescriptor> descriptors)
    {
        var entities = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var module in descriptors)
        {
            foreach (var entity in module.CustomFieldEntities)
            {
                if (!Identifiers.IsLowerCamel(entity.Code) || entity.Code.Length > Domain.Configuration.CustomFieldDefinition.EntityTypeMaxLength)
                {
                    throw new InvalidOperationException($"Custom field entity '{entity.Code}' of module '{module.Code}' must be lower camel case.");
                }

                if (!entities.TryAdd(entity.Code, module.Code))
                {
                    throw new InvalidOperationException($"Custom field entity '{entity.Code}' is declared twice.");
                }
            }
        }

        return entities;
    }

    /// <summary>Permission codes are <c>&lt;module&gt;.…</c>, unique, and navigation entries use their module's permissions.</summary>
    private static Dictionary<string, string> BuildPermissions(IEnumerable<IModuleDescriptor> descriptors)
    {
        var permissions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var module in descriptors)
        {
            foreach (var permission in module.Permissions)
            {
                if (!PermissionCodes.IsValid(permission.Code, module.Code))
                {
                    throw new InvalidOperationException(
                        $"Permission '{permission.Code}' of module '{module.Code}' must be '{module.Code}.<area>.<action>' in lower camel case (at most {Domain.Identity.PermissionEntry.CodeMaxLength}).");
                }

                if (!permissions.TryAdd(permission.Code, module.Code))
                {
                    throw new InvalidOperationException($"Permission '{permission.Code}' is declared twice.");
                }
            }

            var unknown = module.Navigation.FirstOrDefault(entry => entry.Permission is { } code && permissions.GetValueOrDefault(code) != module.Code);
            if (unknown is not null)
            {
                throw new InvalidOperationException($"Navigation entry '{unknown.Key}' of module '{module.Code}' needs permission '{unknown.Permission}', which the module does not declare.");
            }
        }

        return permissions;
    }
}

internal static class PermissionCodes
{
    public static bool IsValid(string code, string moduleCode)
    {
        if (string.IsNullOrEmpty(code) || code.Length > Domain.Identity.PermissionEntry.CodeMaxLength)
        {
            return false;
        }

        var parts = code.Split('.');
        return parts.Length == 3
            && parts[0] == moduleCode
            && parts.Skip(1).All(part => part.Length > 0 && char.IsAsciiLetterLower(part[0]) && part.All(char.IsAsciiLetterOrDigit));
    }
}

internal static class Identifiers
{
    public static bool IsLowerCamel(string value) =>
        !string.IsNullOrEmpty(value) && char.IsAsciiLetterLower(value[0]) && value.All(char.IsAsciiLetterOrDigit);
}

internal static class ModuleCodes
{
    public const int MaxLength = Domain.Platform.PlatformModule.CodeMaxLength;

    public static bool IsValid(string code) =>
        !string.IsNullOrEmpty(code) && code.Length <= MaxLength && code.All(character => character is >= 'a' and <= 'z');
}
