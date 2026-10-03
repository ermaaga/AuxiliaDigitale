using Microsoft.Extensions.Logging;

namespace Auxilia.Diagnostics;

public static partial class Log
{
    public static partial class Documents
    {
        [LoggerMessage(EventId = EventCodes.Documents.StorageUnavailable, EventName = "Documents.StorageUnavailable",
            Level = LogLevel.Error, Message = "Storage provider {Provider} is not configured or has no adapter")]
        public static partial void StorageUnavailable(ILogger logger, string provider);

        [LoggerMessage(EventId = EventCodes.Documents.StorageOperationFailed, EventName = "Documents.StorageOperationFailed",
            Level = LogLevel.Error, Message = "Storage {Provider} failed to {Action} {Key}")]
        public static partial void StorageOperationFailed(ILogger logger, Exception exception, string provider, string action, string key);

        [LoggerMessage(EventId = EventCodes.Documents.FileCommitted, EventName = "Documents.FileCommitted",
            Level = LogLevel.Information, Message = "File {Key} committed ({Size} bytes, {ContentType})")]
        public static partial void FileCommitted(ILogger logger, string key, long size, string contentType);

        [LoggerMessage(EventId = EventCodes.Documents.DocumentIntegrityFailed, EventName = "Documents.DocumentIntegrityFailed",
            Level = LogLevel.Warning, Message = "Document {DocumentId}: the stored file is missing or does not match the upload checksum")]
        public static partial void DocumentIntegrityFailed(ILogger logger, Guid documentId);
    }
}
