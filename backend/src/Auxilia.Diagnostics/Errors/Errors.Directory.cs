using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Directory
    {
        public static Error SpecializationInvalid(string field, string messageKey) =>
            Error.Validation(EventCodes.Directory.SpecializationInvalid, $"The specialization {field} is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [messageKey] });

        public static Error SpecializationNotFound() =>
            Error.NotFound(EventCodes.Directory.SpecializationNotFound, "Specialization not found");

        public static Error SpecializationNameTaken() =>
            Error.Conflict(EventCodes.Directory.SpecializationNameTaken, "The role already has a specialization with this name");

        public static Error SpecializationMemberInvalid() =>
            Error.Validation(EventCodes.Directory.SpecializationMemberInvalid, "Every user must exist and have the specialization's role",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["userIds"] = ["validation.specializations.member"] });

        public static Error PersonInvalid(string field, string messageKey) =>
            Error.Validation(EventCodes.Directory.PersonInvalid, $"The value of {field} is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [messageKey] });

        public static Error PersonInvalid(IReadOnlyDictionary<string, string[]> errors) =>
            Error.Validation(EventCodes.Directory.PersonInvalid, "The person is not valid", errors);

        public static Error ClientNotFound() =>
            Error.NotFound(EventCodes.Directory.ClientNotFound, "Client not found");

        public static Error FiscalCodeTaken() =>
            Error.Conflict(EventCodes.Directory.FiscalCodeTaken, "Another client has this fiscal code");

        public static Error ClientEmployeeRequired() =>
            Error.Conflict(EventCodes.Directory.ClientEmployeeRequired, "Assign an employee before enabling the client's sign-in");

        public static Error EmployeeInvalid() =>
            Error.Validation(EventCodes.Directory.EmployeeInvalid, "The employee must be an active user with the Employee role",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["employeeUserId"] = ["validation.clients.employee"] });

        public static Error ClientSpecializationInvalid() =>
            Error.Validation(EventCodes.Directory.ClientSpecializationInvalid, "Every specialization must be an active Client specialization",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["specializationIds"] = ["validation.clients.specializations"] });
    }
}
