using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Auxilia.Application.Identity;

/// <summary>Users, roles, authentication and sessions (F01, F17, F35). Core: every tenant has it.</summary>
public sealed class IdentityModule : IModuleDescriptor
{
    public const string ModuleCode = "identity";

    public string Code => ModuleCode;

    public ModuleKind Kind => ModuleKind.Core;

    public int EventCodeRangeStart => 12000;

    public IReadOnlyList<PermissionDefinition> Permissions { get; } = [];

    public IReadOnlyList<SettingDefinition> Settings { get; } = IdentitySettings.All;

    public IReadOnlyList<NavigationEntry> Navigation { get; } =
    [
        new("sessions", "/sessions", "monitor-smartphone", 90, [TenantRole.Administrator]),
    ];

    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IUserAccountManager, UserAccountManager>();
        services.TryAddScoped<IPasswordAuthenticator, PasswordAuthenticator>();
    }
}
