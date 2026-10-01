using Microsoft.Extensions.Logging;

namespace Auxilia.Diagnostics;

public static partial class Log
{
    public static partial class Messaging
    {
        [LoggerMessage(EventId = EventCodes.Messaging.DeliveryAttemptFailed, EventName = "Messaging.DeliveryAttemptFailed",
            Level = LogLevel.Warning, Message = "Delivery attempt {Attempt} of outbound message {OutboundMessageId} failed; it will be retried")]
        public static partial void DeliveryAttemptFailed(ILogger logger, Exception exception, int attempt, Guid outboundMessageId);

        [LoggerMessage(EventId = EventCodes.Messaging.MessageFailed, EventName = "Messaging.MessageFailed",
            Level = LogLevel.Warning, Message = "Outbound message {OutboundMessageId} failed permanently with {ErrorCode}")]
        public static partial void MessageFailed(ILogger logger, Guid outboundMessageId, string errorCode);

        [LoggerMessage(EventId = EventCodes.Messaging.TestDeliveryFailed, EventName = "Messaging.TestDeliveryFailed",
            Level = LogLevel.Warning, Message = "Test message {OutboundMessageId} through account {MessagingAccountId} could not be delivered")]
        public static partial void TestDeliveryFailed(ILogger logger, Exception exception, Guid outboundMessageId, Guid messagingAccountId);
    }
}
