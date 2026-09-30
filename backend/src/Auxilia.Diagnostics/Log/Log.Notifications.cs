using Microsoft.Extensions.Logging;

namespace Auxilia.Diagnostics;

public static partial class Log
{
    public static partial class Notifications
    {
        [LoggerMessage(EventId = EventCodes.Notifications.RealtimeConnected, EventName = "Notifications.RealtimeConnected",
            Level = LogLevel.Debug, Message = "Realtime connection {ConnectionId} of user {UserId} joined its groups")]
        public static partial void RealtimeConnected(ILogger logger, string connectionId, Guid userId);

        [LoggerMessage(EventId = EventCodes.Notifications.RealtimeConnectionRejected, EventName = "Notifications.RealtimeConnectionRejected",
            Level = LogLevel.Warning, Message = "Realtime connection {ConnectionId} rejected ({Reason})")]
        public static partial void RealtimeConnectionRejected(ILogger logger, string connectionId, string reason);

        [LoggerMessage(EventId = EventCodes.Notifications.RealtimePushFailed, EventName = "Notifications.RealtimePushFailed",
            Level = LogLevel.Warning, Message = "Realtime push {EventName} to {Target} failed")]
        public static partial void RealtimePushFailed(ILogger logger, Exception exception, string eventName, string target);
    }
}
