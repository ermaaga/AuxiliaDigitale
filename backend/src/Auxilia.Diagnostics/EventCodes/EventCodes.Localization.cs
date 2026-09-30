namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(21000, "Localization")]
    public static class Localization
    {
        /// <summary>No translation key with this id or name (404).</summary>
        public const int ResourceKeyNotFound = 21001;

        /// <summary>A translation key with the same name already exists (409).</summary>
        public const int ResourceKeyExists = 21002;

        /// <summary>The language does not exist or is not active in the tenant (404).</summary>
        public const int LanguageNotFound = 21003;

        /// <summary>A key, category, description or translation value is not valid (400).</summary>
        public const int ResourceValueInvalid = 21004;

        /// <summary>A translation key was created (System console).</summary>
        public const int ResourceKeyCreated = 21005;

        /// <summary>The category or description of a translation key changed.</summary>
        public const int ResourceKeyUpdated = 21006;

        /// <summary>A translation key and its translations were deleted.</summary>
        public const int ResourceKeyDeleted = 21007;

        /// <summary>A translation was set (and marked as customised for the tenant).</summary>
        public const int TranslationSet = 21008;

        /// <summary>A translation was removed (the fallback language applies).</summary>
        public const int TranslationRemoved = 21009;

        /// <summary>A server-side text (e-mail, PDF, notification) asked for a key without any translation.</summary>
        public const int MissingKey = 21010;

        /// <summary>The key has no translation in this language (404).</summary>
        public const int TranslationNotFound = 21011;
    }
}
