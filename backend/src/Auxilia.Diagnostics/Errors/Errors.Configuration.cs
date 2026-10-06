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

        public static Error BrandingImageInvalid() =>
            Error.Validation(EventCodes.Configuration.BrandingImageInvalid, "The image must be a PNG, JPEG or WebP file",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["file"] = ["validation.branding.imageType"] });

        public static Error BrandingImageTooLarge(int maxKilobytes) =>
            Error.Validation(EventCodes.Configuration.BrandingImageTooLarge, $"The image must not be larger than {maxKilobytes} KB",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["file"] = ["validation.branding.imageTooLarge"] });

        public static Error CustomFieldInvalid(string field, string messageKey) =>
            Error.Validation(EventCodes.Configuration.CustomFieldInvalid, $"The custom field {field} is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [messageKey] });

        public static Error CustomFieldKeyTaken() =>
            Error.Conflict(EventCodes.Configuration.CustomFieldKeyTaken, "The entity already has a custom field with this key");

        public static Error CustomFieldNotFound() =>
            Error.NotFound(EventCodes.Configuration.CustomFieldNotFound, "Custom field not found");

        /// <param name="errors">Per field: <c>customFields.&lt;key&gt;</c> → message keys.</param>
        public static Error CustomFieldValuesInvalid(IReadOnlyDictionary<string, string[]> errors) =>
            Error.Validation(EventCodes.Configuration.CustomFieldValuesInvalid, "The custom field values are not valid", errors);

        public static Error GridNotFound(string key) =>
            Error.NotFound(EventCodes.Configuration.GridNotFound, $"Grid {key} not found");

        public static Error GridLayoutInvalid(string messageKey) =>
            Error.Validation(EventCodes.Configuration.GridLayoutInvalid, "The grid layout is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["columns"] = [messageKey] });

        public static Error GridViewInvalid(IReadOnlyDictionary<string, string[]> errors) =>
            Error.Validation(EventCodes.Configuration.GridViewInvalid, "The view is not valid", errors);

        public static Error GridViewNotFound() =>
            Error.NotFound(EventCodes.Configuration.GridViewNotFound, "View not found");

        public static Error BrandingAssetNotFound() =>
            Error.NotFound(EventCodes.Configuration.BrandingAssetNotFound, "Branding image not found");
    }
}
