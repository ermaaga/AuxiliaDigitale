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

        /// <summary>Documents were uploaded for a client (and possibly a case and folder, F14/F33).</summary>
        public const int DocumentsUploaded = 16008;

        /// <summary>The name, reference year, area, description or custom fields of a document changed.</summary>
        public const int DocumentUpdated = 16009;

        /// <summary>A document was moved to another folder of its case (or to none, F33).</summary>
        public const int DocumentMoved = 16010;

        /// <summary>A document was deleted (row and file).</summary>
        public const int DocumentDeleted = 16011;

        /// <summary>A value of a document is not valid (400, field errors).</summary>
        public const int DocumentInvalid = 16012;

        /// <summary>No document with this id visible to the caller (404).</summary>
        public const int DocumentNotFound = 16013;

        /// <summary>Another document of the same client, case and folder has this name (409).</summary>
        public const int DocumentNameTaken = 16014;

        /// <summary>A document area was created (F14: areas are a managed list).</summary>
        public const int DocumentAreaCreated = 16015;

        /// <summary>A document area was renamed or (de)activated.</summary>
        public const int DocumentAreaUpdated = 16016;

        /// <summary>A value of a document area is not valid (400).</summary>
        public const int DocumentAreaInvalid = 16017;

        /// <summary>No document area with this id (404).</summary>
        public const int DocumentAreaNotFound = 16018;

        /// <summary>Another active area has this name (409).</summary>
        public const int DocumentAreaNameTaken = 16019;

        /// <summary>A stored document was checked after the upload (Worker): available or damaged.</summary>
        public const int DocumentProcessed = 16020;

        /// <summary>The file of a document is missing in the storage, or a ZIP has no file (404, legacy "FileNotFound").</summary>
        public const int DocumentFileMissing = 16021;

        /// <summary>The stored file does not match the checksum of the upload: the document is marked damaged.</summary>
        public const int DocumentIntegrityFailed = 16022;
    }
}
