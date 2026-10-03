using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Requests
    {
        public static Error RequestInvalid(IReadOnlyDictionary<string, string[]> errors) =>
            Error.Validation(EventCodes.Requests.RequestInvalid, "The request is not valid", errors);

        public static Error RequestNotFound() =>
            Error.NotFound(EventCodes.Requests.RequestNotFound, "Request not found");

        public static Error RequestIsClosed() =>
            Error.Conflict(EventCodes.Requests.RequestIsClosed, "The request is closed");
    }
}
