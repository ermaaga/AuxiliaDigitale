namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(23000, "Message bus (Rebus / RabbitMQ)")]
    public static class Bus
    {
        /// <summary>A message was handled.</summary>
        public const int MessageHandled = 23001;

        /// <summary>The message was already handled (duplicate delivery): acknowledged without running the handler again.</summary>
        public const int DuplicateMessageSkipped = 23002;

        /// <summary>A tenant message arrived without the <c>x-tenant-slug</c> header: moved to the error queue.</summary>
        public const int MessageTenantMissing = 23003;

        /// <summary>The tenant of the message does not exist or is not active: moved to the error queue.</summary>
        public const int MessageTenantUnavailable = 23004;

        /// <summary>The handler refused the message (permanent failure, e.g. validation or not found): no retries.</summary>
        public const int MessageRejected = 23005;

        /// <summary>The message failed every immediate retry and is retried later (second-level retry).</summary>
        public const int MessageRetryScheduled = 23006;

        /// <summary>The message is moved to the error queue after the last retry or a permanent failure.</summary>
        public const int MessageDeadLettered = 23007;

        /// <summary>A message committed to the outbox could not be sent; it stays pending (manual job <c>messaging.outbox</c>).</summary>
        public const int OutboxDispatchFailed = 23008;
    }
}
