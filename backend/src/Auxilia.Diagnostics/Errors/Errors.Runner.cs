using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Runner
    {
        public static Error TenantMigrationFailed(string slug) =>
            Error.Failure(EventCodes.Runner.TenantMigrationFailed, $"The migration of tenant {slug} failed");

        public static Error LegacySourceUnavailable() =>
            Error.Failure(EventCodes.Runner.LegacySourceUnavailable, "The legacy database cannot be reached");

        /// <param name="missing">Missing tables and columns (<c>Table.Column</c>), comma separated.</param>
        public static Error LegacySchemaUnsupported(string missing) =>
            Error.Failure(EventCodes.Runner.LegacySchemaUnsupported, $"The legacy database is not a supported baseline; missing: {missing}");
    }
}
