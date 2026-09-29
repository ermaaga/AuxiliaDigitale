namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(24000, "Cache / Redis")]
    public static class Cache
    {
        /// <summary>Redis (L2 cache) failed: the circuit opens and the in-memory cache plus the database serve reads.</summary>
        public const int CacheBackendUnavailable = 24001;

        /// <summary>Redis answers again after <see cref="CacheBackendUnavailable"/>.</summary>
        public const int CacheBackendRecovered = 24002;

        /// <summary>A cache invalidation could not be published to the other nodes (their L1 expires by itself).</summary>
        public const int InvalidationPublishFailed = 24003;

        /// <summary>The node could not subscribe to the invalidation channel (retried when the connection is restored).</summary>
        public const int InvalidationSubscriptionFailed = 24004;
    }
}
