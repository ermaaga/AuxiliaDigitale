using Microsoft.Extensions.Logging;

namespace Auxilia.Diagnostics;

public static partial class Log
{
    public static partial class Localization
    {
        [LoggerMessage(EventId = EventCodes.Localization.MissingKey, EventName = "Localization.MissingKey",
            Level = LogLevel.Warning, Message = "Translation key {ResourceKey} has no translation (language {LanguageCode}); the key is shown")]
        public static partial void MissingKey(ILogger logger, string resourceKey, string languageCode);
    }
}
