using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Application.Scheduling;

/// <summary>Appointments (F13).</summary>
public sealed class SchedulingModule : IModuleDescriptor
{
    public const string ModuleCode = "scheduling";

    public string Code => ModuleCode;

    public ModuleKind Kind => ModuleKind.Optional;

    public int EventCodeRangeStart => 15000;

    public IReadOnlyList<PermissionDefinition> Permissions { get; } = [];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public IReadOnlyList<NavigationEntry> Navigation { get; } =
    [
        new("appointments", "/appointments", "calendar", 50, [TenantRole.Employee, TenantRole.Client]),
    ];

    public void AddServices(IServiceCollection services)
    {
    }
}
