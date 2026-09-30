using Auxilia.Application.Abstractions.Authorization;
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

    public IReadOnlyList<PermissionDefinition> Permissions { get; } = IdentityPermissions.All;

    public IReadOnlyList<SettingDefinition> Settings { get; } = IdentitySettings.All;

    public IReadOnlyList<NavigationEntry> Navigation { get; } =
    [
        new("sessions", "/sessions", "monitor-smartphone", 90, [TenantRole.Administrator], IdentityPermissions.ViewSessions),
    ];

    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IUserAccountManager, UserAccountManager>();
        services.TryAddScoped<IPasswordAuthenticator, PasswordAuthenticator>();
        services.TryAddScoped<ClientApplicationValidator>();
        services.TryAddScoped<ISessionManager, SessionManager>();
        services.TryAddScoped<IAccountLinkManager, AccountLinkManager>();
        services.TryAddScoped<ISigningKeyManager, SigningKeyManager>();
        services.TryAddScoped<IClientApplicationManager, ClientApplicationManager>();
        services.TryAddScoped<RolePermissionsCache>();
        services.TryAddScoped<IPermissionAccess, PermissionAccess>();
        services.TryAddScoped<IAccessGuard, AccessGuard>();
        services.TryAddScoped<ICurrentUserQueryService, CurrentUserQueryService>();
    }
}
