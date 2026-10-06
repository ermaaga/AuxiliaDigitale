using Microsoft.Extensions.Logging;

namespace Auxilia.Diagnostics;

public static partial class Log
{
    public static partial class Runner
    {
        [LoggerMessage(EventId = EventCodes.Runner.DataMigrationApplied, EventName = "Runner.DataMigrationApplied",
            Level = LogLevel.Information, Message = "Data-migration {Key} applied in {DurationMs} ms: {Description}")]
        public static partial void DataMigrationApplied(ILogger logger, string key, long durationMs, string description);

        [LoggerMessage(EventId = EventCodes.Runner.DataMigrationFailed, EventName = "Runner.DataMigrationFailed",
            Level = LogLevel.Error, Message = "Data-migration {Key} failed")]
        public static partial void DataMigrationFailed(ILogger logger, Exception exception, string key);

        [LoggerMessage(EventId = EventCodes.Runner.LegacyInspected, EventName = "Runner.LegacyInspected",
            Level = LogLevel.Information,
            Message = "Legacy database inspected: last migration {LastMigration}, Security_Update {SecurityUpdate}, {Rows} rows in {Tables} tables")]
        public static partial void LegacyInspected(ILogger logger, string? lastMigration, bool securityUpdate, long rows, int tables);

        [LoggerMessage(EventId = EventCodes.Runner.LegacyImported, EventName = "Runner.LegacyImported",
            Level = LogLevel.Information,
            Message = "Legacy import ended (dry run {DryRun}): {Created} created, {Updated} updated, {Skipped} skipped, {Warnings} warnings")]
        public static partial void LegacyImported(ILogger logger, bool dryRun, int created, int updated, int skipped, int warnings);

        [LoggerMessage(EventId = EventCodes.Runner.LegacyReconciliationFailed, EventName = "Runner.LegacyReconciliationFailed",
            Level = LogLevel.Warning, Message = "Legacy reconciliation found {Differences} differences: {Checks}")]
        public static partial void LegacyReconciliationFailed(ILogger logger, int differences, string checks);
    }
}
