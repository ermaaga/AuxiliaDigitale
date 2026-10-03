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

        public static Error DocumentInvalid(IReadOnlyDictionary<string, string[]> errors) =>
            Error.Validation(EventCodes.Documents.DocumentInvalid, "The document is not valid", errors);

        public static Error DocumentInvalid(string field, string messageKey) =>
            DocumentInvalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [messageKey] });

        public static Error DocumentNotFound() =>
            Error.NotFound(EventCodes.Documents.DocumentNotFound, "Document not found");

        public static Error DocumentNameTaken() =>
            Error.Conflict(EventCodes.Documents.DocumentNameTaken, "Another document here has this name");

        public static Error DocumentAreaInvalid() =>
            Error.Validation(EventCodes.Documents.DocumentAreaInvalid, "The area name is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["name"] = ["validation.documentAreas.name"] });

        public static Error DocumentAreaNotFound() =>
            Error.NotFound(EventCodes.Documents.DocumentAreaNotFound, "Document area not found");

        public static Error DocumentAreaNameTaken() =>
            Error.Conflict(EventCodes.Documents.DocumentAreaNameTaken, "Another active area has this name");

        public static Error DocumentFileMissing() =>
            Error.NotFound(EventCodes.Documents.DocumentFileMissing, "The file is not available");
    }
}
