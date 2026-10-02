using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Tenancy
    {
        public static Error CrossTenantAttempt() =>
            Error.Forbidden(EventCodes.Tenancy.CrossTenantAttempt, "The token does not belong to the requested tenant");

        public static Error TenantRequired() =>
            Error.Validation(EventCodes.Tenancy.TenantRequired, "The request does not identify a tenant",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["tenant"] = ["validation.tenant.required"] });

        public static Error TenantNotFound() =>
            Error.NotFound(EventCodes.Tenancy.TenantNotFound, "Tenant not found");

        public static Error TenantAlreadyExists(string slug) =>
            Error.Conflict(EventCodes.Tenancy.TenantAlreadyExists, $"Tenant {slug} already exists");

        public static Error TenantSlugReserved() =>
            Error.Validation(EventCodes.Tenancy.TenantSlugReserved, "The tenant slug is reserved",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["slug"] = ["validation.tenant.slugReserved"] });

        public static Error TenantDatabaseInvalid() =>
            Error.Failure(EventCodes.Tenancy.TenantDatabaseInvalid, "The tenant database cannot be used");

        public static Error TenantSlugInvalid() =>
            Error.Validation(EventCodes.Tenancy.TenantSlugInvalid, "The tenant slug is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["slug"] = ["validation.tenant.slug"] });

        public static Error TenantTransitionNotAllowed(string from, string to) =>
            Error.Conflict(EventCodes.Tenancy.TenantTransitionNotAllowed, $"A tenant cannot move from {from} to {to}");

        public static Error PlanNotFound() =>
            Error.NotFound(EventCodes.Tenancy.PlanNotFound, "Plan not found");

        public static Error ModuleNotFound() =>
            Error.NotFound(EventCodes.Tenancy.ModuleNotFound, "Module not found");

        public static Error CoreModuleNotConfigurable() =>
            Error.Validation(EventCodes.Tenancy.CoreModuleNotConfigurable, "Core modules are always visible and take no override",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["moduleCode"] = ["validation.tenant.coreModule"] });

        public static Error TenantNotProvisioning() =>
            Error.Conflict(EventCodes.Tenancy.TenantNotProvisioning, "The tenant is not waiting for provisioning");

        public static Error TenantArchived() =>
            Error.Conflict(EventCodes.Tenancy.TenantArchived, "The tenant is archived and cannot be changed");

        public static Error LogLevelUntilInvalid() =>
            Error.Validation(EventCodes.Tenancy.LogLevelUntilInvalid, "The debug logging must end within the next 24 hours",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["until"] = ["validation.logs.until"] });

        public static Error LogQueryInvalid(string field, string translationKey) =>
            Error.Validation(EventCodes.Tenancy.LogQueryInvalid, $"The log search value {field} is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [translationKey] });

        public static Error LogFilesUnavailable() =>
            Error.Failure(EventCodes.Tenancy.LogFilesUnavailable, "The log files cannot be read now");

        public static Error CatalogValueInvalid(string field, string translationKey) =>
            Error.Validation(EventCodes.Tenancy.CatalogValueInvalid, $"The value of {field} is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [translationKey] });
    }
}
