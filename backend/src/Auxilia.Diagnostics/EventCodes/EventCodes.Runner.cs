namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(28000, "MigrationRunner / Legacy import")]
    public static class Runner
    {
        /// <summary>A tenant data-migration was applied.</summary>
        public const int DataMigrationApplied = 28001;

        /// <summary>A tenant data-migration failed; the run stops and the tenant keeps the previous data version.</summary>
        public const int DataMigrationFailed = 28002;

        /// <summary>The Catalog schema was migrated.</summary>
        public const int CatalogMigrated = 28003;

        /// <summary>A tenant database was migrated (schema, then data-migrations).</summary>
        public const int TenantMigrated = 28004;

        /// <summary>A tenant migration failed; the tenant is marked MigrationFailed.</summary>
        public const int TenantMigrationFailed = 28005;
    }
}
