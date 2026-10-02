namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(13000, "Directory (clients, employees)")]
    public static class Directory
    {
        /// <summary>A role specialization was created (F12).</summary>
        public const int SpecializationCreated = 13001;

        /// <summary>A role specialization was changed.</summary>
        public const int SpecializationUpdated = 13002;

        /// <summary>A role specialization was deactivated (it keeps its members and disappears from the lists).</summary>
        public const int SpecializationDeactivated = 13003;

        /// <summary>A role specialization is not valid (name, role, e-mail…) (400).</summary>
        public const int SpecializationInvalid = 13004;

        /// <summary>No active specialization with this id (404).</summary>
        public const int SpecializationNotFound = 13005;

        /// <summary>The role already has an active specialization with this name (409).</summary>
        public const int SpecializationNameTaken = 13006;

        /// <summary>Users were added to a specialization.</summary>
        public const int SpecializationMembersAdded = 13007;

        /// <summary>A user was removed from a specialization.</summary>
        public const int SpecializationMemberRemoved = 13008;

        /// <summary>A user to add does not exist or does not have the specialization's role (400).</summary>
        public const int SpecializationMemberInvalid = 13009;

        /// <summary>A client was created (person, profile and user account, F05).</summary>
        public const int ClientCreated = 13010;

        /// <summary>The personal data, user name or custom fields of a client changed.</summary>
        public const int ClientUpdated = 13011;

        /// <summary>A client was deleted (soft delete, Q29): hidden from lists, sign-in disabled.</summary>
        public const int ClientDeleted = 13012;

        /// <summary>A client's sign-in was enabled or disabled (D-05).</summary>
        public const int ClientSignInChanged = 13013;

        /// <summary>The employee assigned to a client changed or was removed (history kept).</summary>
        public const int ClientAssignmentChanged = 13014;

        /// <summary>The specializations of a client changed (Q30: many).</summary>
        public const int ClientSpecializationsChanged = 13015;

        /// <summary>A value of a person (client, employee) is not valid (400, field errors).</summary>
        public const int PersonInvalid = 13016;

        /// <summary>No client with this id (404).</summary>
        public const int ClientNotFound = 13017;

        /// <summary>Another person (client or employee) has the fiscal code (409, Q54).</summary>
        public const int FiscalCodeTaken = 13018;

        /// <summary>Sign-in cannot be enabled for a client without an assigned employee (409, Q60).</summary>
        public const int ClientEmployeeRequired = 13019;

        /// <summary>The employee to assign is not an active user with the Employee role (400).</summary>
        public const int EmployeeInvalid = 13020;

        /// <summary>A specialization to give to a client is not an active Client specialization (400).</summary>
        public const int ClientSpecializationInvalid = 13021;

        /// <summary>An employee was created (person, employee profile and user account, F06).</summary>
        public const int EmployeeCreated = 13022;

        /// <summary>The personal data or user name of an employee changed.</summary>
        public const int EmployeeUpdated = 13023;

        /// <summary>An employee was deleted (soft delete): sign-in disabled, clients handed to the default employee.</summary>
        public const int EmployeeDeleted = 13024;

        /// <summary>An employee's sign-in was enabled or disabled.</summary>
        public const int EmployeeSignInChanged = 13025;

        /// <summary>Another employee became the default one (Q31).</summary>
        public const int DefaultEmployeeChanged = 13026;

        /// <summary>The specializations of an employee changed.</summary>
        public const int EmployeeSpecializationsChanged = 13027;

        /// <summary>The administrator an employee reports to changed or was removed (Q32).</summary>
        public const int EmployeeAdministratorChanged = 13028;

        /// <summary>No employee with this id (404).</summary>
        public const int EmployeeNotFound = 13029;

        /// <summary>The default employee cannot be disabled or deleted: choose another default first (409, Q31).</summary>
        public const int EmployeeIsDefault = 13030;

        /// <summary>Only an employee who can sign in can become the default one (409).</summary>
        public const int DefaultEmployeeInactive = 13031;

        /// <summary>A specialization to give to an employee is not an active Employee specialization (400).</summary>
        public const int EmployeeSpecializationInvalid = 13032;

        /// <summary>The administrator to report to is not an active user with the Administrator role (400, Q32).</summary>
        public const int AdministratorInvalid = 13033;
    }
}
