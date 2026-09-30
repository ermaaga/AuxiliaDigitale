using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Localization
    {
        public static Error ResourceKeyNotFound() =>
            Error.NotFound(EventCodes.Localization.ResourceKeyNotFound, "Translation key not found");

        public static Error ResourceKeyExists(string key) =>
            Error.Conflict(EventCodes.Localization.ResourceKeyExists, $"The translation key {key} already exists");

        public static Error LanguageNotFound(string language) =>
            Error.NotFound(EventCodes.Localization.LanguageNotFound, $"Language {language} is not available");

        public static Error TranslationNotFound(string language) =>
            Error.NotFound(EventCodes.Localization.TranslationNotFound, $"The key has no translation in {language}");

        public static Error ResourceValueInvalid(string field) =>
            Error.Validation(EventCodes.Localization.ResourceValueInvalid, $"The field {field} is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = ["validation.localization." + field] });
    }
}
