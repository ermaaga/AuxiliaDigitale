using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Configuration
    {
        public static Error SettingNotFound(string key) =>
            Error.NotFound(EventCodes.Configuration.SettingNotFound, $"Setting {key} does not exist");

        public static Error SettingScopeNotAllowed(string key, string level) =>
            Error.Validation(EventCodes.Configuration.SettingScopeNotAllowed, $"Setting {key} cannot be set at level {level}",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["level"] = ["validation.settings.levelNotAllowed"] });

        public static Error SettingValueInvalid(string key) =>
            Error.Validation(EventCodes.Configuration.SettingValueInvalid, $"The value of setting {key} is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["value"] = ["validation.settings.valueInvalid"] });
    }
}
