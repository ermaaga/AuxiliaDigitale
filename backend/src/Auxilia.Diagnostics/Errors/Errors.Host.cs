using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Host
    {
        public static Error Unexpected() =>
            Error.Failure(EventCodes.Host.UnhandledException, "An unexpected error occurred");

        public static Error ConcurrencyConflict() =>
            Error.Conflict(EventCodes.Host.ConcurrencyConflict, "The resource was modified by another request");

        public static Error PreconditionFailed() =>
            Error.PreconditionFailed(EventCodes.Host.PreconditionFailed, "The resource version does not match If-Match");

        public static Error IdempotencyKeyReused() =>
            Error.Conflict(EventCodes.Host.IdempotencyKeyReused, "The idempotency key was already used with a different payload");

        public static Error DatabaseTimeout() =>
            Error.Failure(EventCodes.Host.DatabaseTimeout, "The database did not respond in time");
    }
}
