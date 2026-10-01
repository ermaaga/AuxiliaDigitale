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
    }
}
