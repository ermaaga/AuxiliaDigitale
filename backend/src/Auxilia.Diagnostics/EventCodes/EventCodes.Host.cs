namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(10000, "Host / Startup / Middleware")]
    public static class Host
    {
        /// <summary>Unhandled exception caught by the global handler (500).</summary>
        public const int UnhandledException = 10001;

        // 10002–10009 are left free on purpose: the foundation codes below are fixed by ADR 0012.

        /// <summary>Optimistic concurrency conflict, <c>DbUpdateConcurrencyException</c> (409).</summary>
        public const int ConcurrencyConflict = 10010;

        /// <summary><c>If-Match</c> does not match the current version (412).</summary>
        public const int PreconditionFailed = 10011;

        /// <summary>Idempotency key reused with a different payload (409).</summary>
        public const int IdempotencyKeyReused = 10012;

        /// <summary>A database command timed out (500).</summary>
        public const int DatabaseTimeout = 10013;

        /// <summary>The client cancelled the request; not an error.</summary>
        public const int RequestCancelled = 10014;
    }
}
