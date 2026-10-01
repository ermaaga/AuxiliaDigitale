using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Application.Cases;

/// <summary>Service catalog, cases, payments, status history, service folders (F08–F11, F33).</summary>
public sealed class CasesModule : IModuleDescriptor
{
    public const string ModuleCode = "cases";

    public string Code => ModuleCode;

    public ModuleKind Kind => ModuleKind.Optional;

    public int EventCodeRangeStart => 14000;

    public IReadOnlyList<PermissionDefinition> Permissions { get; } = CasesPermissions.All;

    public IReadOnlyList<SettingDefinition> Settings { get; } = CasesSettings.All;

    public IReadOnlyList<NavigationEntry> Navigation { get; } =
    [
        new("cases", "/cases", "briefcase", 30, [TenantRole.Administrator, TenantRole.Employee, TenantRole.Client], CasesPermissions.ViewCases),
        new("services", "/services", "layers", 40, [TenantRole.Administrator], CasesPermissions.ManageServices),
    ];

    public IReadOnlyList<CustomFieldEntityDefinition> CustomFieldEntities { get; } = [new("case")];

    public void AddServices(IServiceCollection services)
    {
    }
}
