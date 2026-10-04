using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Marketing
    {
        public static Error SegmentInvalid(IReadOnlyDictionary<string, string[]> errors) =>
            Error.Validation(EventCodes.Marketing.SegmentInvalid, "The segment is not valid", errors);

        public static Error SegmentInvalid(string field, string messageKey) =>
            SegmentInvalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [messageKey] });

        public static Error SegmentNotFound() =>
            Error.NotFound(EventCodes.Marketing.SegmentNotFound, "Segment not found");

        public static Error ListInvalid(string field, string messageKey) =>
            Error.Validation(EventCodes.Marketing.ListInvalid, "The list is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [messageKey] });

        public static Error ListNotFound() =>
            Error.NotFound(EventCodes.Marketing.ListNotFound, "List not found");
    }
}
