namespace Auxilia.Diagnostics;

public static partial class Operations
{
    public static class Documents
    {
        public static readonly OperationDescriptor Upload = new("Documents.Upload", EventCodes.Documents.DocumentsUploaded);

        public static readonly OperationDescriptor Update = new("Documents.Update", EventCodes.Documents.DocumentUpdated);

        public static readonly OperationDescriptor Move = new("Documents.Move", EventCodes.Documents.DocumentMoved);

        public static readonly OperationDescriptor Delete = new("Documents.Delete", EventCodes.Documents.DocumentDeleted);

        public static readonly OperationDescriptor Process = new("Documents.Process", EventCodes.Documents.DocumentProcessed);

        public static readonly OperationDescriptor CreateArea = new("Documents.CreateArea", EventCodes.Documents.DocumentAreaCreated);

        public static readonly OperationDescriptor UpdateArea = new("Documents.UpdateArea", EventCodes.Documents.DocumentAreaUpdated);
    }
}
