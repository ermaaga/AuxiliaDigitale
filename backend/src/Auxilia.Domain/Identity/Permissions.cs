using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Domain.Identity;

/// <summary>
/// A permission known to the tenant (<c>identity.permissions</c>), synchronised from the module descriptors: a
/// permission seen for the first time is granted to its default roles; later the System may change the grants (S-01)
/// and the synchronisation never re-grants what was removed.
/// </summary>
public sealed class PermissionEntry
{
    public const int CodeMaxLength = 100;
    public const int ModuleCodeMaxLength = 50;

    public PermissionEntry(string code, string moduleCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleCode);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(code.Length, CodeMaxLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(moduleCode.Length, ModuleCodeMaxLength);

        Code = code;
        ModuleCode = moduleCode;
    }

    private PermissionEntry()
    {
        Code = ModuleCode = string.Empty;
    }

    /// <summary><c>&lt;module&gt;.&lt;area&gt;.&lt;action&gt;</c>, e.g. <c>cases.cases.view</c>.</summary>
    public string Code { get; private set; }

    public string ModuleCode { get; private set; }
}

/// <summary>A grant of a permission to a tenant role (<c>identity.role_permissions</c>).</summary>
public sealed class RoleGrant
{
    public RoleGrant(TenantRole role, string permissionCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionCode);

        Role = role;
        PermissionCode = permissionCode;
    }

    private RoleGrant()
    {
        PermissionCode = string.Empty;
    }

    public TenantRole Role { get; private set; }

    public string PermissionCode { get; private set; }
}
