namespace Auxilia.Diagnostics;

public static partial class Operations
{
    public static class Directory
    {
        public static readonly OperationDescriptor CreateSpecialization = new("Directory.CreateSpecialization", EventCodes.Directory.SpecializationCreated);

        public static readonly OperationDescriptor UpdateSpecialization = new("Directory.UpdateSpecialization", EventCodes.Directory.SpecializationUpdated);

        public static readonly OperationDescriptor DeactivateSpecialization = new("Directory.DeactivateSpecialization", EventCodes.Directory.SpecializationDeactivated);

        public static readonly OperationDescriptor AddSpecializationMembers = new("Directory.AddSpecializationMembers", EventCodes.Directory.SpecializationMembersAdded);

        public static readonly OperationDescriptor RemoveSpecializationMember = new("Directory.RemoveSpecializationMember", EventCodes.Directory.SpecializationMemberRemoved);
    }
}
