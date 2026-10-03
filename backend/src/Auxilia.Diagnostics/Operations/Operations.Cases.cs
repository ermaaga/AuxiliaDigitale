namespace Auxilia.Diagnostics;

public static partial class Operations
{
    public static class Cases
    {
        public static readonly OperationDescriptor CreateServiceCategory = new("Cases.CreateServiceCategory", EventCodes.Cases.ServiceCategoryCreated);

        public static readonly OperationDescriptor UpdateServiceCategory = new("Cases.UpdateServiceCategory", EventCodes.Cases.ServiceCategoryUpdated);

        public static readonly OperationDescriptor DeleteServiceCategory = new("Cases.DeleteServiceCategory", EventCodes.Cases.ServiceCategoryDeleted);

        public static readonly OperationDescriptor CreateService = new("Cases.CreateService", EventCodes.Cases.ServiceCreated);

        public static readonly OperationDescriptor UpdateService = new("Cases.UpdateService", EventCodes.Cases.ServiceUpdated);

        public static readonly OperationDescriptor DeleteService = new("Cases.DeleteService", EventCodes.Cases.ServiceDeleted);

        public static readonly OperationDescriptor OpenCase = new("Cases.OpenCase", EventCodes.Cases.CaseOpened);

        public static readonly OperationDescriptor AdvanceCase = new("Cases.AdvanceCase", EventCodes.Cases.CaseAdvanced);

        public static readonly OperationDescriptor MoveCaseBack = new("Cases.MoveCaseBack", EventCodes.Cases.CaseMovedBack);

        public static readonly OperationDescriptor CompleteCase = new("Cases.CompleteCase", EventCodes.Cases.CaseCompleted);

        public static readonly OperationDescriptor RecordCasePayment = new("Cases.RecordCasePayment", EventCodes.Cases.CasePaymentRecorded);

        public static readonly OperationDescriptor UpdateCase = new("Cases.UpdateCase", EventCodes.Cases.CaseUpdated);

        public static readonly OperationDescriptor DeleteCase = new("Cases.DeleteCase", EventCodes.Cases.CaseDeleted);
    }
}
