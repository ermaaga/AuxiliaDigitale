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

        public static readonly OperationDescriptor CreateClient = new("Directory.CreateClient", EventCodes.Directory.ClientCreated);

        public static readonly OperationDescriptor UpdateClient = new("Directory.UpdateClient", EventCodes.Directory.ClientUpdated);

        public static readonly OperationDescriptor DeleteClient = new("Directory.DeleteClient", EventCodes.Directory.ClientDeleted);

        public static readonly OperationDescriptor ChangeClientSignIn = new("Directory.ChangeClientSignIn", EventCodes.Directory.ClientSignInChanged);

        public static readonly OperationDescriptor ChangeClientAssignment = new("Directory.ChangeClientAssignment", EventCodes.Directory.ClientAssignmentChanged);

        public static readonly OperationDescriptor ChangeClientSpecializations = new("Directory.ChangeClientSpecializations", EventCodes.Directory.ClientSpecializationsChanged);

        public static readonly OperationDescriptor CreateEmployee = new("Directory.CreateEmployee", EventCodes.Directory.EmployeeCreated);

        public static readonly OperationDescriptor UpdateEmployee = new("Directory.UpdateEmployee", EventCodes.Directory.EmployeeUpdated);

        public static readonly OperationDescriptor DeleteEmployee = new("Directory.DeleteEmployee", EventCodes.Directory.EmployeeDeleted);

        public static readonly OperationDescriptor ChangeEmployeeSignIn = new("Directory.ChangeEmployeeSignIn", EventCodes.Directory.EmployeeSignInChanged);

        public static readonly OperationDescriptor ChangeDefaultEmployee = new("Directory.ChangeDefaultEmployee", EventCodes.Directory.DefaultEmployeeChanged);

        public static readonly OperationDescriptor ChangeEmployeeSpecializations = new("Directory.ChangeEmployeeSpecializations", EventCodes.Directory.EmployeeSpecializationsChanged);

        public static readonly OperationDescriptor ChangeEmployeeAdministrator = new("Directory.ChangeEmployeeAdministrator", EventCodes.Directory.EmployeeAdministratorChanged);
    }
}
