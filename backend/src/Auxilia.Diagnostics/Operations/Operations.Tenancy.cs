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
    }
}
