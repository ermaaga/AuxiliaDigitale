namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(18000, "Engagement: notifications / realtime")]
    public static class Notifications
    {
        /// <summary>A client connected to the realtime hub and joined its tenant, user, role and session groups.</summary>
        public const int RealtimeConnected = 18001;

        /// <summary>A hub connection was refused: token without tenant, user or session, or tenant not active.</summary>
        public const int RealtimeConnectionRejected = 18002;

        /// <summary>A realtime push failed (e.g. backplane unavailable); the operation that produced it is not affected.</summary>
        public const int RealtimePushFailed = 18003;

        /// <summary>Notifications were created for their recipients (F16): rows, realtime push and e-mails by preference.</summary>
        public const int NotificationsSent = 18004;

        /// <summary>A user marked one of their notifications as read.</summary>
        public const int NotificationRead = 18005;

        /// <summary>A user marked all their notifications as read.</summary>
        public const int AllNotificationsRead = 18006;

        /// <summary>A user deleted one of their notifications.</summary>
        public const int NotificationDeleted = 18007;

        /// <summary>A user changed their notification preferences.</summary>
        public const int NotificationPreferencesSaved = 18008;

        /// <summary>No notification with this id among the caller's (404).</summary>
        public const int NotificationNotFound = 18009;

        /// <summary>A notification preference is not valid (400: unknown kind).</summary>
        public const int NotificationPreferencesInvalid = 18010;
    }
}
