namespace Auxilia.Domain.Platform;

/// <summary>Lifecycle of a tenant (skill auxilia-multitenancy); only <see cref="Active"/> serves traffic.</summary>
public enum TenantStatus
{
    Provisioning,
    Active,
    Suspended,
    MigrationFailed,

    /// <summary>Terminal: tenants are archived, never deleted by the system (D-25).</summary>
    Archived,
}
