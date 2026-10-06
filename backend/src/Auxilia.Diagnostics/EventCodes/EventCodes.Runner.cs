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

        /// <summary>The legacy database cannot be reached with the configured connection.</summary>
        public const int LegacySourceUnavailable = 28006;

        /// <summary>The legacy database lacks tables or columns of the supported baseline (D-30): the import does not run.</summary>
        public const int LegacySchemaUnsupported = 28007;

        /// <summary>The legacy database was inspected (schema variant, rows per table).</summary>
        public const int LegacyInspected = 28008;

        /// <summary>A legacy import run ended (committed, or rolled back as a dry run), with its counts.</summary>
        public const int LegacyImported = 28009;

        /// <summary>The reconciliation of a legacy import found differences: the cutover is blocked until they are explained.</summary>
        public const int LegacyReconciliationFailed = 28010;
    }
}
