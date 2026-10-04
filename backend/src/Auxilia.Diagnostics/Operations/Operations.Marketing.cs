namespace Auxilia.Diagnostics;

public static partial class Operations
{
    public static class Marketing
    {
        public static readonly OperationDescriptor CreateSegment = new("Marketing.CreateSegment", EventCodes.Marketing.SegmentCreated);

        public static readonly OperationDescriptor UpdateSegment = new("Marketing.UpdateSegment", EventCodes.Marketing.SegmentUpdated);

        public static readonly OperationDescriptor DeleteSegment = new("Marketing.DeleteSegment", EventCodes.Marketing.SegmentDeleted);

        public static readonly OperationDescriptor CreateList = new("Marketing.CreateList", EventCodes.Marketing.ListCreated);

        public static readonly OperationDescriptor UpdateList = new("Marketing.UpdateList", EventCodes.Marketing.ListUpdated);

        public static readonly OperationDescriptor DeleteList = new("Marketing.DeleteList", EventCodes.Marketing.ListDeleted);

        public static readonly OperationDescriptor ChangeListMembers = new("Marketing.ChangeListMembers", EventCodes.Marketing.ListMembersChanged);
    }
}
