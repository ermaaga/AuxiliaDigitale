namespace Auxilia.Diagnostics;

public static partial class Operations
{
    public static class Requests
    {
        public static readonly OperationDescriptor CreateRequest = new("Requests.CreateRequest", EventCodes.Requests.RequestCreated);

        public static readonly OperationDescriptor ReplyToRequest = new("Requests.ReplyToRequest", EventCodes.Requests.RequestReplied);

        public static readonly OperationDescriptor CloseRequest = new("Requests.CloseRequest", EventCodes.Requests.RequestClosed);

        public static readonly OperationDescriptor DeleteRequest = new("Requests.DeleteRequest", EventCodes.Requests.RequestDeleted);

        public static readonly OperationDescriptor CreateTask = new("Requests.CreateTask", EventCodes.Requests.TaskCreated);

        public static readonly OperationDescriptor UpdateTask = new("Requests.UpdateTask", EventCodes.Requests.TaskUpdated);

        public static readonly OperationDescriptor CompleteTask = new("Requests.CompleteTask", EventCodes.Requests.TaskCompleted);

        public static readonly OperationDescriptor ReopenTask = new("Requests.ReopenTask", EventCodes.Requests.TaskReopened);

        public static readonly OperationDescriptor DeleteTask = new("Requests.DeleteTask", EventCodes.Requests.TaskDeleted);

        public static readonly OperationDescriptor AddActivity = new("Requests.AddActivity", EventCodes.Requests.ActivityAdded);

        public static readonly OperationDescriptor DeleteActivity = new("Requests.DeleteActivity", EventCodes.Requests.ActivityDeleted);
    }
}
