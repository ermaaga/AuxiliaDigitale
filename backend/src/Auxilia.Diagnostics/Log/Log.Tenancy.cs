using Microsoft.Extensions.Logging;

namespace Auxilia.Diagnostics;

public static partial class Log
{
    public static partial class Tenancy
    {
        [LoggerMessage(EventId = EventCodes.Tenancy.ProvisioningNotDispatched, EventName = "Tenancy.ProvisioningNotDispatched",
            Level = LogLevel.Warning, Message = "The provisioning of tenant {TenantSlug} was not queued (message bus unavailable): retry from the console")]
        public static partial void ProvisioningNotDispatched(ILogger logger, Exception exception, string tenantSlug);

        [LoggerMessage(EventId = EventCodes.Tenancy.LogFilesUnavailable, EventName = "Tenancy.LogFilesUnavailable",
            Level = LogLevel.Error, Message = "The log files of tenant {TenantSlug} could not be read")]
        public static partial void LogFilesUnavailable(ILogger logger, Exception exception, string tenantSlug);

        [LoggerMessage(EventId = EventCodes.Tenancy.LogLevelSyncFailed, EventName = "Tenancy.LogLevelSyncFailed",
            Level = LogLevel.Warning, Message = "The log levels of the tenants could not be synchronised ({Step}): this node keeps the levels it knows")]
        public static partial void LogLevelSyncFailed(ILogger logger, Exception exception, string step);
    }
}
