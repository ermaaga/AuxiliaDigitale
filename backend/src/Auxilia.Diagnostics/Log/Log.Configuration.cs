using Microsoft.Extensions.Logging;

namespace Auxilia.Diagnostics;

public static partial class Log
{
    public static partial class Configuration
    {
        [LoggerMessage(EventId = EventCodes.Configuration.StoredSettingIgnored, EventName = "Configuration.StoredSettingIgnored",
            Level = LogLevel.Warning, Message = "Stored value of setting {SettingKey} at level {SettingLevel} is not valid and is ignored")]
        public static partial void StoredSettingIgnored(ILogger logger, string settingKey, string settingLevel);
    }
}
