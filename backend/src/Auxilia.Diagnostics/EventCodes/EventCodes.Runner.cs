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
    }
}
