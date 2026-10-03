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
    }
}
