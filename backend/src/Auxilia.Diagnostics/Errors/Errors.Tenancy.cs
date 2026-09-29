using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Tenancy
    {
        public static Error CrossTenantAttempt() =>
            Error.Forbidden(EventCodes.Tenancy.CrossTenantAttempt, "The token does not belong to the requested tenant");
    }
}
