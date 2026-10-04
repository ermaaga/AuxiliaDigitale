using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Cases
    {
        public static Error ServiceCategoryInvalid(string field, string messageKey) =>
            Error.Validation(EventCodes.Cases.ServiceCategoryInvalid, $"The category {field} is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [messageKey] });

        public static Error ServiceCategoryNotFound() =>
            Error.NotFound(EventCodes.Cases.ServiceCategoryNotFound, "Service category not found");

        public static Error ServiceCategoryNameTaken() =>
            Error.Conflict(EventCodes.Cases.ServiceCategoryNameTaken, "Another active category has this name");

        public static Error ServiceCategoryInUse() =>
            Error.Conflict(EventCodes.Cases.ServiceCategoryInUse, "The category is used by a service: deactivate it instead");

        public static Error ServiceInvalid(IReadOnlyDictionary<string, string[]> errors) =>
            Error.Validation(EventCodes.Cases.ServiceInvalid, "The service is not valid", errors);

        public static Error ServiceInvalid(string field, string messageKey) =>
            ServiceInvalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [messageKey] });

        public static Error ServiceNotFound() =>
            Error.NotFound(EventCodes.Cases.ServiceNotFound, "Service not found");

        public static Error ServiceNameTaken() =>
            Error.Conflict(EventCodes.Cases.ServiceNameTaken, "Another service has this name");

        public static Error CaseInvalid(IReadOnlyDictionary<string, string[]> errors) =>
            Error.Validation(EventCodes.Cases.CaseInvalid, "The case is not valid", errors);

        public static Error CaseInvalid(string field, string messageKey) =>
            CaseInvalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [messageKey] });

        public static Error CaseNotFound() =>
            Error.NotFound(EventCodes.Cases.CaseNotFound, "Case not found");

        public static Error CaseIsCompleted() =>
            Error.Conflict(EventCodes.Cases.CaseIsCompleted, "The case is completed and cannot change any more");

        public static Error CaseCompletionRequired() =>
            Error.Conflict(EventCodes.Cases.CaseCompletionRequired, "Complete the case with the amount received and the outcome");

        public static Error CaseCannotGoBack() =>
            Error.Conflict(EventCodes.Cases.CaseCannotGoBack, "An inserted case cannot move back");

        public static Error ServiceInUse() =>
            Error.Conflict(EventCodes.Cases.ServiceInUse, "The service has cases: deactivate it instead");

        public static Error ServiceFolderInvalid(string field, string messageKey) =>
            Error.Validation(EventCodes.Cases.ServiceFolderInvalid, $"The folder {field} is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [messageKey] });

        public static Error ServiceFolderNotFound() =>
            Error.NotFound(EventCodes.Cases.ServiceFolderNotFound, "Folder not found");

        public static Error CaseHasNoEndDate() =>
            Error.Conflict(EventCodes.Cases.CaseHasNoEndDate, "The case has neither an expiry nor a due date");

        public static Error CaseClientHasNoEmail() =>
            Error.Conflict(EventCodes.Cases.CaseClientHasNoEmail, "The client of the case has no e-mail address");

        public static Error ServiceChecklistInvalid(IReadOnlyDictionary<string, string[]> errors) =>
            Error.Validation(EventCodes.Cases.ServiceChecklistInvalid, "The checklist is not valid", errors);

        public static Error CaseChecklistItemNotFound() =>
            Error.NotFound(EventCodes.Cases.CaseChecklistItemNotFound, "The item is not in the checklist of the case");

        public static Error ServiceFolderNameTaken() =>
            Error.Conflict(EventCodes.Cases.ServiceFolderNameTaken, "A folder with this name already exists here");
    }
}
