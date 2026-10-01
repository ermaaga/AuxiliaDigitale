using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Identity;
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
        new("loginAudit", "/login-audit", "shield-check", 95, [TenantRole.Administrator], IdentityPermissions.ViewLoginAttempts),
    ];

    public IReadOnlyList<GridDefinition> Grids { get; } =
    [
        new(
            "identity.loginAttempts",
            [
                new("attemptedAt", "Date", Sortable: true, CanHide: false),
                new("userName", "Username", Sortable: true, Filterable: true),
                new("method", "LoginType", Filterable: true),
                new("result", "LoginResult", Filterable: true),
                new("failureReason", "FailureReason"),
                new("ipAddress", "app.identity.loginAttempts.ipAddress"),
                new("userAgent", "app.identity.loginAttempts.userAgent", VisibleByDefault: false),
            ],
            [TenantRole.Administrator]),
    ];

    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IPasswordPolicy, PasswordPolicy>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthenticationMethod, PasswordAuthenticationMethod>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthenticationMethod, EmailOtpAuthenticationMethod>());
        services.TryAddScoped<ILoginAuditQueryService, LoginAuditQueryService>();
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
        services.TryAddScoped<IRolePermissionManager, RolePermissionManager>();
        services.TryAddScoped<IRolePermissionQueryService, RolePermissionQueryService>();
        services.TryAddScoped<ICurrentUserQueryService, CurrentUserQueryService>();
        services.TryAddScoped<PlatformAuthManager>();
        services.TryAddScoped<IPlatformAuthManager>(provider => provider.GetRequiredService<PlatformAuthManager>());
        services.TryAddScoped<IPlatformSessionEnder>(provider => provider.GetRequiredService<PlatformAuthManager>());
        services.TryAddScoped<IPlatformUserManager, PlatformUserManager>();
    }
}
