namespace Auxilia.Domain.Platform;

/// <summary>Tenant roles for which plans and overrides enable a module (the tenant role SystemConfigurator no longer exists, D-18).</summary>
public enum TenantRole
{
    Administrator,
    Employee,
    Client,
}
