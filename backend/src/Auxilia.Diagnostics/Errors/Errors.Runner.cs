using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Runner
    {
        public static Error TenantMigrationFailed(string slug) =>
            Error.Failure(EventCodes.Runner.TenantMigrationFailed, $"The migration of tenant {slug} failed");
    }
}
