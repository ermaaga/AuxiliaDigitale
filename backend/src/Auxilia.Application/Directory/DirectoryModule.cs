using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Application.Directory;

/// <summary>Clients, employees, assignments, specializations, tags, consents, registration requests (F02–F06, F12).</summary>
public sealed class DirectoryModule : IModuleDescriptor
{
    public const string ModuleCode = "directory";

    public string Code => ModuleCode;

    public ModuleKind Kind => ModuleKind.Optional;

    public int EventCodeRangeStart => 13000;

    public IReadOnlyList<PermissionDefinition> Permissions { get; } = DirectoryPermissions.All;

    public IReadOnlyList<SettingDefinition> Settings { get; } = DirectorySettings.All;

    public IReadOnlyList<NavigationEntry> Navigation { get; } =
    [
        new("clients", "/clients", "users", 10, [TenantRole.Administrator, TenantRole.Employee], DirectoryPermissions.ViewClients),
        new("employees", "/employees", "user-cog", 20, [TenantRole.Administrator], DirectoryPermissions.ViewEmployees),
    ];

    public IReadOnlyList<CustomFieldEntityDefinition> CustomFieldEntities { get; } = [new("client")];

    public void AddServices(IServiceCollection services)
    {
    }
}
