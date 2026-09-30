using Auxilia.Application.Abstractions.Modules;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Identity;

/// <summary>Permissions of the identity module (<c>identity.role_permissions</c>, F22); default roles reproduce the legacy pages.</summary>
public static class IdentityPermissions
{
    /// <summary>Accounts of employees and clients: activation links, roles, deactivation (F01, F05, F06).</summary>
    public const string ManageUsers = "identity.users.manage";

    /// <summary>Active sessions (F17).</summary>
    public const string ViewSessions = "identity.sessions.view";

    /// <summary>End another user's session (F17).</summary>
    public const string RevokeSessions = "identity.sessions.revoke";

    /// <summary>The login audit: every sign-in attempt (F35).</summary>
    public const string ViewLoginAttempts = "identity.loginAttempts.view";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(ManageUsers, [TenantRole.Administrator]),
        new(ViewSessions, [TenantRole.Administrator]),
        new(RevokeSessions, [TenantRole.Administrator]),
        new(ViewLoginAttempts, [TenantRole.Administrator]),
    ];
}
