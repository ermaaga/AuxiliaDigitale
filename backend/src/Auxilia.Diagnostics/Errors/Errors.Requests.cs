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

        public static Error TaskInvalid(IReadOnlyDictionary<string, string[]> errors) =>
            Error.Validation(EventCodes.Requests.TaskInvalid, "The task is not valid", errors);

        public static Error TaskInvalid(string field, string messageKey) =>
            TaskInvalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [messageKey] });

        public static Error TaskNotFound() =>
            Error.NotFound(EventCodes.Requests.TaskNotFound, "Task not found");

        public static Error ActivityInvalid(IReadOnlyDictionary<string, string[]> errors) =>
            Error.Validation(EventCodes.Requests.ActivityInvalid, "The activity is not valid", errors);

        public static Error ActivityNotFound() =>
            Error.NotFound(EventCodes.Requests.ActivityNotFound, "Activity not found");
    }
}
