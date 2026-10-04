namespace Auxilia.Diagnostics;

public static partial class Operations
{
    public static class Imports
    {
        public static readonly OperationDescriptor CreateImportType = new("Imports.CreateImportType", EventCodes.Imports.ImportTypeCreated);

        public static readonly OperationDescriptor DeleteImportType = new("Imports.DeleteImportType", EventCodes.Imports.ImportTypeDeleted);

        public static readonly OperationDescriptor StartImport = new("Imports.StartImport", EventCodes.Imports.ImportStarted);

        /// <summary>Not a write operation: progress is saved in batches by write operations of its own.</summary>
        public static readonly OperationDescriptor ValidateImport = new("Imports.ValidateImport", EventCodes.Imports.ImportValidated, isWrite: false);

        public static readonly OperationDescriptor ConfirmImport = new("Imports.ConfirmImport", EventCodes.Imports.ImportConfirmed);

        /// <summary>Not a write operation: every row is imported by the operation of its module, on its own.</summary>
        public static readonly OperationDescriptor ProcessImport = new("Imports.ProcessImport", EventCodes.Imports.ImportProcessed, isWrite: false);

        public static readonly OperationDescriptor SaveImportProgress = new("Imports.SaveImportProgress", EventCodes.Imports.ImportProgressSaved);

        public static readonly OperationDescriptor CancelImport = new("Imports.CancelImport", EventCodes.Imports.ImportCancelled);

        public static readonly OperationDescriptor DeleteImport = new("Imports.DeleteImport", EventCodes.Imports.ImportDeleted);
    }
}
