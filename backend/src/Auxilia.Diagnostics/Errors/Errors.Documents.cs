using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Documents
    {
        public static Error FileTypeNotAllowed() =>
            Error.Validation(EventCodes.Documents.FileTypeNotAllowed, "This file type is not accepted",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["file"] = ["validation.documents.fileType"] });

        public static Error FileContentMismatch() =>
            Error.Validation(EventCodes.Documents.FileContentMismatch, "The content of the file does not match its type",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["file"] = ["validation.documents.fileContent"] });

        public static Error FileTooLarge() =>
            Error.Validation(EventCodes.Documents.FileTooLarge, "The file is too large",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["file"] = ["validation.documents.fileSize"] });

        public static Error FileInvalid() =>
            Error.Validation(EventCodes.Documents.FileInvalid, "The file is empty or has no name",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["file"] = ["validation.documents.file"] });

        public static Error StorageUnavailable() =>
            Error.Failure(EventCodes.Documents.StorageUnavailable, "The document storage is not available");
    }
}
