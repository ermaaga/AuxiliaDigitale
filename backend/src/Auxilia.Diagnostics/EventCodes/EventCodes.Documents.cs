namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(16000, "Documents / Storage")]
    public static class Documents
    {
        /// <summary>The file type (extension) is not accepted (400, whitelist F14).</summary>
        public const int FileTypeNotAllowed = 16001;

        /// <summary>The content does not match the file type (magic bytes, 400).</summary>
        public const int FileContentMismatch = 16002;

        /// <summary>The file is larger than <c>documents.maxUploadMb</c> (400, Q49).</summary>
        public const int FileTooLarge = 16003;

        /// <summary>The file is empty or has no usable name (400).</summary>
        public const int FileInvalid = 16004;

        /// <summary>The storage provider of the tenant is not configured or not available (500).</summary>
        public const int StorageUnavailable = 16005;

        /// <summary>A storage operation failed (logged with the provider and the key).</summary>
        public const int StorageOperationFailed = 16006;

        /// <summary>A file was moved from staging to its final key (B-11 staging → commit).</summary>
        public const int FileCommitted = 16007;
    }
}
