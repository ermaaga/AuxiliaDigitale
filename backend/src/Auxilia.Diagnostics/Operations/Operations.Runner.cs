namespace Auxilia.Diagnostics;

public static partial class Operations
{
    public static class Runner
    {
        public static readonly OperationDescriptor MigrateCatalog = new("Runner.MigrateCatalog", EventCodes.Runner.CatalogMigrated);

        public static readonly OperationDescriptor MigrateTenant = new("Runner.MigrateTenant", EventCodes.Runner.TenantMigrated);
    }
}
