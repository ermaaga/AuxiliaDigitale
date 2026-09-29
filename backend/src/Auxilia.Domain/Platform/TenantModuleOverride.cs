namespace Auxilia.Domain.Platform;

/// <summary>
/// System override of a module for one tenant (catalog <c>tenant_module_overrides</c>, D-18): enabled or disabled,
/// optionally for specific roles. When present it replaces what the plan says for that module.
/// </summary>
public sealed class TenantModuleOverride
{
    public TenantModuleOverride(Guid tenantId, string moduleCode, bool isEnabled, IReadOnlyCollection<TenantRole> roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleCode);
        ArgumentNullException.ThrowIfNull(roles);

        TenantId = tenantId;
        ModuleCode = moduleCode;
        IsEnabled = isEnabled;
        Roles = isEnabled ? roles.Distinct().Order().ToArray() : [];
    }

    private TenantModuleOverride()
    {
        ModuleCode = string.Empty;
        Roles = [];
    }

    public Guid TenantId { get; private set; }

    public string ModuleCode { get; private set; }

    public bool IsEnabled { get; private set; }

    /// <summary>Roles that see the module when enabled.</summary>
    public TenantRole[] Roles { get; private set; }

    public void Set(bool isEnabled, IReadOnlyCollection<TenantRole> roles)
    {
        IsEnabled = isEnabled;
        Roles = isEnabled ? roles.Distinct().Order().ToArray() : [];
    }
}
