namespace Auxilia.Diagnostics;

public static partial class Operations
{
    public static class Tenancy
    {
        public static readonly OperationDescriptor ProvisionTenant = new("Tenancy.ProvisionTenant", EventCodes.Tenancy.TenantProvisioned);

        public static readonly OperationDescriptor SuspendTenant = new("Tenancy.SuspendTenant", EventCodes.Tenancy.TenantWasSuspended);

        public static readonly OperationDescriptor ReactivateTenant = new("Tenancy.ReactivateTenant", EventCodes.Tenancy.TenantWasReactivated);

        public static readonly OperationDescriptor SyncModules = new("Tenancy.SyncModules", EventCodes.Tenancy.ModulesSynchronized);

        public static readonly OperationDescriptor ArchiveTenant = new("Tenancy.ArchiveTenant", EventCodes.Tenancy.TenantWasArchived);

        public static readonly OperationDescriptor RequestProvisioning = new("Tenancy.RequestProvisioning", EventCodes.Tenancy.TenantProvisioningRequested);

        public static readonly OperationDescriptor UpdateTenant = new("Tenancy.UpdateTenant", EventCodes.Tenancy.TenantUpdated);

        public static readonly OperationDescriptor ChangeTenantPlan = new("Tenancy.ChangeTenantPlan", EventCodes.Tenancy.TenantPlanChanged);

        public static readonly OperationDescriptor ChangeModuleOverride = new("Tenancy.ChangeModuleOverride", EventCodes.Tenancy.TenantModuleOverrideChanged);

        public static readonly OperationDescriptor ChangeLogLevel = new("Tenancy.ChangeLogLevel", EventCodes.Tenancy.TenantLogLevelChanged);
    }
}
