using Microsoft.Extensions.Logging;

namespace Auxilia.Diagnostics;

public static partial class Log
{
    public static partial class Tenancy
    {
        [LoggerMessage(EventId = EventCodes.Tenancy.ProvisioningNotDispatched, EventName = "Tenancy.ProvisioningNotDispatched",
            Level = LogLevel.Warning, Message = "The provisioning of tenant {TenantSlug} was not queued (message bus unavailable): retry from the console")]
        public static partial void ProvisioningNotDispatched(ILogger logger, Exception exception, string tenantSlug);
    }
}
