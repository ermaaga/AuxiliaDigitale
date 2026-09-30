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

        /// <summary>The log file storage failed; events go to the console and the local buffer (written by the file sink).</summary>
        public const int LogStorageUnavailable = 10015;

        /// <summary>The log file storage works again after <see cref="LogStorageUnavailable"/> (written by the file sink).</summary>
        public const int LogStorageRecovered = 10016;

        /// <summary>No endpoint matches the route (404).</summary>
        public const int EndpointNotFound = 10017;

        /// <summary>The route exists but not for this HTTP method (405).</summary>
        public const int MethodNotAllowed = 10018;

        /// <summary>The request body, route or query values cannot be read (400), e.g. malformed JSON.</summary>
        public const int RequestInvalid = 10019;

        /// <summary>The request fails validation (400); field errors are translation keys.</summary>
        public const int ValidationFailed = 10020;

        /// <summary>An action registered to run after an operation committed (e.g. cache invalidation) failed; the operation stays successful.</summary>
        public const int PostCommitActionFailed = 10021;
    }
}
