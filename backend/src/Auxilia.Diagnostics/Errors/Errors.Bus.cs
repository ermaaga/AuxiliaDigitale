using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Bus
    {
        public static Error MessageTenantMissing() =>
            Error.Validation(EventCodes.Bus.MessageTenantMissing, "The tenant message has no tenant header",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["x-tenant-slug"] = ["validation.bus.tenantMissing"] });

        public static Error MessageTenantUnavailable(string slug) =>
            Error.NotFound(EventCodes.Bus.MessageTenantUnavailable, $"Tenant {slug} does not exist or is not active");
    }
}
