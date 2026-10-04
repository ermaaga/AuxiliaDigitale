using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Imports
    {
        public static Error ImportTypeInvalid(string field, string messageKey) =>
            Error.Validation(EventCodes.Imports.ImportTypeInvalid, $"The import type {field} is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [messageKey] });

        public static Error ImportTypeNotFound() =>
            Error.NotFound(EventCodes.Imports.ImportTypeNotFound, "Import type not found");

        public static Error ImportTypeInUse() =>
            Error.Conflict(EventCodes.Imports.ImportTypeInUse, "The import type has imports: delete them first");

        public static Error ImportInvalid(IReadOnlyDictionary<string, string[]> errors) =>
            Error.Validation(EventCodes.Imports.ImportInvalid, "The import is not valid", errors);

        public static Error ImportInvalid(string field, string messageKey) =>
            ImportInvalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [messageKey] });

        public static Error ImportNotFound() =>
            Error.NotFound(EventCodes.Imports.ImportNotFound, "Import not found");

        public static Error ImportFileUnreadable(string reason) =>
            Error.Failure(EventCodes.Imports.ImportFileUnreadable, $"The import file cannot be read: {reason}");

        public static Error ImportStatusInvalid(string status) =>
            Error.Conflict(EventCodes.Imports.ImportStatusInvalid, $"The import is {status}: this is not possible now");
    }
}
