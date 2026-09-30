using Microsoft.Extensions.Logging;

namespace Auxilia.Diagnostics;

public static partial class Log
{
    public static partial class Bus
    {
        [LoggerMessage(EventId = EventCodes.Bus.DuplicateMessageSkipped, EventName = "Bus.DuplicateMessageSkipped",
            Level = LogLevel.Information, Message = "Message {MessageId} already handled by {Handler}: skipped")]
        public static partial void DuplicateMessageSkipped(ILogger logger, Guid messageId, string handler);

        [LoggerMessage(EventId = EventCodes.Bus.MessageTenantMissing, EventName = "Bus.MessageTenantMissing",
            Level = LogLevel.Error, Message = "Tenant message {MessageType} {MessageId} has no tenant header")]
        public static partial void MessageTenantMissing(ILogger logger, string messageType, string messageId);

        [LoggerMessage(EventId = EventCodes.Bus.MessageTenantUnavailable, EventName = "Bus.MessageTenantUnavailable",
            Level = LogLevel.Warning, Message = "Tenant {TenantSlug} of message {MessageType} {MessageId} is missing or not active")]
        public static partial void MessageTenantUnavailable(ILogger logger, string tenantSlug, string messageType, string messageId);

        [LoggerMessage(EventId = EventCodes.Bus.MessageRejected, EventName = "Bus.MessageRejected",
            Level = LogLevel.Warning, Message = "Message {MessageType} {MessageId} rejected by {Handler} with {ErrorCode}: not retried")]
        public static partial void MessageRejected(ILogger logger, string messageType, string messageId, string handler, string errorCode);

        [LoggerMessage(EventId = EventCodes.Bus.MessageRetryScheduled, EventName = "Bus.MessageRetryScheduled",
            Level = LogLevel.Warning, Message = "Message {MessageType} {MessageId} failed; second-level retry {Attempt} in {DelaySeconds} s")]
        public static partial void MessageRetryScheduled(ILogger logger, string messageType, string messageId, int attempt, double delaySeconds);

        [LoggerMessage(EventId = EventCodes.Bus.MessageDeadLettered, EventName = "Bus.MessageDeadLettered",
            Level = LogLevel.Error, Message = "Message {MessageType} {MessageId} moved to the error queue: {Reason}")]
        public static partial void MessageDeadLettered(ILogger logger, string messageType, string messageId, string reason);

        [LoggerMessage(EventId = EventCodes.Bus.OutboxDispatchFailed, EventName = "Bus.OutboxDispatchFailed",
            Level = LogLevel.Warning, Message = "Outbox message {OutboxId} ({MessageType}) not sent; it stays pending")]
        public static partial void OutboxDispatchFailed(ILogger logger, Exception exception, Guid outboxId, string messageType);
    }
}
