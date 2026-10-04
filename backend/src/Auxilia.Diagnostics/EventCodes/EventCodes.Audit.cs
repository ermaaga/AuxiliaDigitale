namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(27000, "Audit / Reporting / Export")]
    public static class Audit
    {
        /// <summary>A list was exported and downloaded at once (F26: CSV, Excel or PDF of every row of the filters).</summary>
        public const int ExportGenerated = 27001;

        /// <summary>A large export was queued for the Worker.</summary>
        public const int ExportQueued = 27002;

        /// <summary>The Worker wrote a queued export; the user is told (notification and realtime).</summary>
        public const int ExportCompleted = 27003;

        /// <summary>No exportable list with this key (404).</summary>
        public const int ExportSourceNotFound = 27004;

        /// <summary>A value of an export request is not valid: format, columns, selected ids (400).</summary>
        public const int ExportInvalid = 27005;

        /// <summary>Too many rows even for a queued export: narrow the filters (400).</summary>
        public const int ExportTooLarge = 27006;

        /// <summary>No export of the caller with this id, or it expired (404).</summary>
        public const int ExportNotFound = 27007;

        /// <summary>The export is still being written or failed (409).</summary>
        public const int ExportNotReady = 27008;
    }
}
