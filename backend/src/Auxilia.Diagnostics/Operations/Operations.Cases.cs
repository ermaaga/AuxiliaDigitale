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
    }
}
