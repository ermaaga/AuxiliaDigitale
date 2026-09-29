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
    }
}
