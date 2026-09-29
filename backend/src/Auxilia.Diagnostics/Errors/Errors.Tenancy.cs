using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Tenancy
    {
        public static Error CrossTenantAttempt() =>
            Error.Forbidden(EventCodes.Tenancy.CrossTenantAttempt, "The token does not belong to the requested tenant");

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
