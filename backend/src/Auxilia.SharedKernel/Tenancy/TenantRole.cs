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
