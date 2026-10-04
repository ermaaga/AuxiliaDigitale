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

        public static readonly OperationDescriptor CreateTemplate = new("Marketing.CreateTemplate", EventCodes.Marketing.TemplateCreated);

        public static readonly OperationDescriptor UpdateTemplate = new("Marketing.UpdateTemplate", EventCodes.Marketing.TemplateUpdated);

        public static readonly OperationDescriptor DeleteTemplate = new("Marketing.DeleteTemplate", EventCodes.Marketing.TemplateDeleted);

        public static readonly OperationDescriptor SendTemplateTest = new("Marketing.SendTemplateTest", EventCodes.Marketing.TemplateTestSent);

        public static readonly OperationDescriptor CreateCampaign = new("Marketing.CreateCampaign", EventCodes.Marketing.CampaignCreated);

        public static readonly OperationDescriptor UpdateCampaign = new("Marketing.UpdateCampaign", EventCodes.Marketing.CampaignUpdated);

        public static readonly OperationDescriptor DeleteCampaign = new("Marketing.DeleteCampaign", EventCodes.Marketing.CampaignDeleted);

        public static readonly OperationDescriptor QueueCampaign = new("Marketing.QueueCampaign", EventCodes.Marketing.CampaignQueued);

        public static readonly OperationDescriptor CancelCampaign = new("Marketing.CancelCampaign", EventCodes.Marketing.CampaignCancelled);

        /// <summary>Not a write operation: the snapshot and every batch are write operations of their own.</summary>
        public static readonly OperationDescriptor SendCampaign = new("Marketing.SendCampaign", EventCodes.Marketing.CampaignSent, isWrite: false);

        public static readonly OperationDescriptor SendCampaignBatch = new("Marketing.SendCampaignBatch", EventCodes.Marketing.CampaignBatchSent);

        public static readonly OperationDescriptor AddSuppression = new("Marketing.AddSuppression", EventCodes.Marketing.SuppressionAdded);

        public static readonly OperationDescriptor RemoveSuppression = new("Marketing.RemoveSuppression", EventCodes.Marketing.SuppressionRemoved);
    }
}
