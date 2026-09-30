using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Application.Tests.Platform.Modules;

/// <summary>A configurable module descriptor for tests.</summary>
internal sealed class TestModule(
    string code,
    ModuleKind kind = ModuleKind.Optional,
    int rangeStart = 90000,
    IReadOnlyList<NavigationEntry>? navigation = null,
    IReadOnlyList<PermissionDefinition>? permissions = null) : IModuleDescriptor
{
    public string Code => code;

    public ModuleKind Kind => kind;

    public int EventCodeRangeStart => rangeStart;

    public IReadOnlyList<PermissionDefinition> Permissions { get; } = permissions ?? [];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public IReadOnlyList<NavigationEntry> Navigation { get; } = navigation ?? [];

    public int ServicesAdded { get; private set; }

    public void AddServices(IServiceCollection services) => ServicesAdded++;
}

internal static class Roles
{
    public const TenantRole Admin = TenantRole.Administrator;
    public const TenantRole Employee = TenantRole.Employee;
    public const TenantRole Client = TenantRole.Client;
}
