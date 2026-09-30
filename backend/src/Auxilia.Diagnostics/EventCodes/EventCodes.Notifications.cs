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
    }
}
