namespace Auxilia.SharedKernel.Tenancy;

/// <summary>
/// Roles of tenant users (<c>identity.roles</c>); also the unit of module visibility in plans and overrides. The
/// tenant role SystemConfigurator no longer exists (D-18): its work belongs to the platform role System.
/// </summary>
public enum TenantRole
{
    Administrator,
    Employee,
    Client,
}

public static class TenantRoles
{
    /// <summary>The exact role name (<c>Administrator</c>, <c>Employee</c>, <c>Client</c>): no other case, no numbers.</summary>
    public static bool TryParse(string? value, out TenantRole role)
    {
        role = default;
        return value is not null
            && Enum.GetNames<TenantRole>().Contains(value, StringComparer.Ordinal)
            && Enum.TryParse(value, ignoreCase: false, out role);
    }
}
