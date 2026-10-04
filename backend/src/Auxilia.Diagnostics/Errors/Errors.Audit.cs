using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Audit
    {
        public static Error SourceNotFound() =>
            Error.NotFound(EventCodes.Audit.ExportSourceNotFound, "No exportable list with this key");

        public static Error Invalid(string field, string messageKey) =>
            Error.Validation(EventCodes.Audit.ExportInvalid, $"The export {field} is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [messageKey] });

        public static Error TooLarge() =>
            Error.Validation(EventCodes.Audit.ExportTooLarge, "Too many rows to export: narrow the filters",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["filter"] = ["validation.exports.tooLarge"] });

        public static Error NotFound() =>
            Error.NotFound(EventCodes.Audit.ExportNotFound, "Export not found or expired");

        public static Error NotReady() =>
            Error.Conflict(EventCodes.Audit.ExportNotReady, "The export is not ready");
    }
}
