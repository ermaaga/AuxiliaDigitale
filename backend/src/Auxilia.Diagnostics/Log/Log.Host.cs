using Microsoft.Extensions.Logging;

namespace Auxilia.Diagnostics;

public static partial class Log
{
    public static partial class Host
    {
        [LoggerMessage(EventId = EventCodes.Host.UnhandledException, EventName = "Host.UnhandledException",
            Level = LogLevel.Error, Message = "Unhandled exception while processing {RequestMethod} {RequestPath}")]
        public static partial void UnhandledException(ILogger logger, Exception exception, string requestMethod, string requestPath);

        [LoggerMessage(EventId = EventCodes.Host.ConcurrencyConflict, EventName = "Host.ConcurrencyConflict",
            Level = LogLevel.Warning, Message = "Concurrency conflict in operation {Operation}")]
        public static partial void ConcurrencyConflict(ILogger logger, string operation);

        [LoggerMessage(EventId = EventCodes.Host.PreconditionFailed, EventName = "Host.PreconditionFailed",
            Level = LogLevel.Warning, Message = "If-Match precondition failed for {RequestMethod} {RequestPath}")]
        public static partial void PreconditionFailed(ILogger logger, string requestMethod, string requestPath);

        [LoggerMessage(EventId = EventCodes.Host.IdempotencyKeyReused, EventName = "Host.IdempotencyKeyReused",
            Level = LogLevel.Warning, Message = "Idempotency key reused with a different payload for {RequestMethod} {RequestPath}")]
        public static partial void IdempotencyKeyReused(ILogger logger, string requestMethod, string requestPath);

        [LoggerMessage(EventId = EventCodes.Host.DatabaseTimeout, EventName = "Host.DatabaseTimeout",
            Level = LogLevel.Error, Message = "Database command timed out in operation {Operation}")]
        public static partial void DatabaseTimeout(ILogger logger, Exception exception, string operation);

        [LoggerMessage(EventId = EventCodes.Host.RequestCancelled, EventName = "Host.RequestCancelled",
            Level = LogLevel.Information, Message = "Request {RequestMethod} {RequestPath} cancelled by the client")]
        public static partial void RequestCancelled(ILogger logger, string requestMethod, string requestPath);

        [LoggerMessage(EventId = EventCodes.Host.PostCommitActionFailed, EventName = "Host.PostCommitActionFailed",
            Level = LogLevel.Warning, Message = "A post-commit action of operation {Operation} failed")]
        public static partial void PostCommitActionFailed(ILogger logger, Exception exception, string operation);
    }
}
