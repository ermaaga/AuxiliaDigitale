using Microsoft.Extensions.Logging;

namespace Auxilia.Diagnostics;

public static partial class Log
{
    public static partial class Tenancy
    {
        [LoggerMessage(EventId = EventCodes.Tenancy.CrossTenantAttempt, EventName = "Tenancy.CrossTenantAttempt",
            Level = LogLevel.Warning, Message = "Cross-tenant attempt: claim tenant {ClaimTenant}, requested tenant {RequestedTenant}")]
        public static partial void CrossTenantAttempt(ILogger logger, string claimTenant, string requestedTenant);
    }
}
