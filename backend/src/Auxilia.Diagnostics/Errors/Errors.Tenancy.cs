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

        public static Error CatalogValueInvalid(string field, string translationKey) =>
            Error.Validation(EventCodes.Tenancy.CatalogValueInvalid, $"The value of {field} is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [translationKey] });
    }
}
