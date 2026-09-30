using Microsoft.Extensions.Logging;

namespace Auxilia.Diagnostics;

public static partial class Log
{
    public static partial class Cache
    {
        [LoggerMessage(EventId = EventCodes.Cache.CacheBackendUnavailable, EventName = "Cache.CacheBackendUnavailable",
            Level = LogLevel.Warning, Message = "Redis cache unavailable; serving from memory and database for {BreakSeconds} s")]
        public static partial void CacheBackendUnavailable(ILogger logger, Exception exception, double breakSeconds);

        [LoggerMessage(EventId = EventCodes.Cache.CacheBackendRecovered, EventName = "Cache.CacheBackendRecovered",
            Level = LogLevel.Information, Message = "Redis cache available again")]
        public static partial void CacheBackendRecovered(ILogger logger);

        [LoggerMessage(EventId = EventCodes.Cache.InvalidationPublishFailed, EventName = "Cache.InvalidationPublishFailed",
            Level = LogLevel.Warning, Message = "Invalidation of cache tag {CacheTag} not published to the other nodes")]
        public static partial void InvalidationPublishFailed(ILogger logger, Exception exception, string cacheTag);

        [LoggerMessage(EventId = EventCodes.Cache.InvalidationSubscriptionFailed, EventName = "Cache.InvalidationSubscriptionFailed",
            Level = LogLevel.Warning, Message = "Subscription to the cache invalidation channel failed")]
        public static partial void InvalidationSubscriptionFailed(ILogger logger, Exception exception);
    }
}
