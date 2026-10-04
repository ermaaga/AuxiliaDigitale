namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(22000, "Imports")]
    public static class Imports
    {
        /// <summary>An import type was created by the System (F19).</summary>
        public const int ImportTypeCreated = 22001;

        /// <summary>An import type without imports was deleted.</summary>
        public const int ImportTypeDeleted = 22002;

        /// <summary>A value of an import type is not valid (400, field errors).</summary>
        public const int ImportTypeInvalid = 22003;

        /// <summary>No import type with this id (404).</summary>
        public const int ImportTypeNotFound = 22004;

        /// <summary>An import type with imports cannot be deleted (409).</summary>
        public const int ImportTypeInUse = 22005;

        /// <summary>A file was uploaded and its import queued for validation (F19).</summary>
        public const int ImportStarted = 22006;

        /// <summary>The import request is not valid: name, type, file type or size (400, field errors).</summary>
        public const int ImportInvalid = 22007;

        /// <summary>No import with this id (404).</summary>
        public const int ImportNotFound = 22008;

        /// <summary>The Worker validated the rows of an import: it waits for confirmation.</summary>
        public const int ImportValidated = 22009;

        /// <summary>The file cannot be read: not an Excel workbook, no rows, missing required columns or too many rows (the import fails).</summary>
        public const int ImportFileUnreadable = 22010;

        /// <summary>The System confirmed an import: the valid rows are being imported.</summary>
        public const int ImportConfirmed = 22011;

        /// <summary>The import is not in a status that allows this (409).</summary>
        public const int ImportStatusInvalid = 22012;

        /// <summary>The Worker imported the valid rows of an import (each row on its own).</summary>
        public const int ImportProcessed = 22013;

        /// <summary>An import waiting for confirmation was cancelled (its rows discarded).</summary>
        public const int ImportCancelled = 22014;

        /// <summary>A finished import was deleted.</summary>
        public const int ImportDeleted = 22015;

        /// <summary>A batch of import rows (validation or import results) and the progress were saved.</summary>
        public const int ImportProgressSaved = 22016;
    }
}
