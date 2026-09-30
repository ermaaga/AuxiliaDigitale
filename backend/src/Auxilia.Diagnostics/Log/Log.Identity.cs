using Microsoft.Extensions.Logging;

namespace Auxilia.Diagnostics;

public static partial class Log
{
    public static partial class Identity
    {
        [LoggerMessage(EventId = EventCodes.Identity.PermissionsSynchronized, EventName = "Identity.PermissionsSynchronized",
            Level = LogLevel.Information, Message = "Permissions aligned with the modules: {Added} added, {Removed} removed, {Granted} default grants")]
        public static partial void PermissionsSynchronized(ILogger logger, int added, int removed, int granted);
    }
}
