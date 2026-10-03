namespace Auxilia.Diagnostics;

public static partial class Operations
{
    public static class Requests
    {
        public static readonly OperationDescriptor CreateRequest = new("Requests.CreateRequest", EventCodes.Requests.RequestCreated);

        public static readonly OperationDescriptor ReplyToRequest = new("Requests.ReplyToRequest", EventCodes.Requests.RequestReplied);

        public static readonly OperationDescriptor CloseRequest = new("Requests.CloseRequest", EventCodes.Requests.RequestClosed);

        public static readonly OperationDescriptor DeleteRequest = new("Requests.DeleteRequest", EventCodes.Requests.RequestDeleted);
    }
}
