namespace Auxilia.Diagnostics;

public static partial class Operations
{
    public static class Audit
    {
        public static readonly OperationDescriptor ExportList = new("Audit.ExportList", EventCodes.Audit.ExportGenerated);

        public static readonly OperationDescriptor QueueExport = new("Audit.QueueExport", EventCodes.Audit.ExportQueued);

        public static readonly OperationDescriptor GenerateExport = new("Audit.GenerateExport", EventCodes.Audit.ExportCompleted);
    }
}
