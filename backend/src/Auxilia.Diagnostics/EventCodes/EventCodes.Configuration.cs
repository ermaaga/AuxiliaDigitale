namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(20000, "Configuration")]
    public static class Configuration
    {
        /// <summary>No setting definition has this key (404).</summary>
        public const int SettingNotFound = 20001;

        /// <summary>The setting cannot be set at the requested level (platform, tenant, user) (400).</summary>
        public const int SettingScopeNotAllowed = 20002;

        /// <summary>The value has the wrong type or fails the validation of the definition (400).</summary>
        public const int SettingValueInvalid = 20003;

        /// <summary>A stored value cannot be read with the current definition and is ignored (the next level applies).</summary>
        public const int StoredSettingIgnored = 20004;

        /// <summary>A setting value was stored at a level.</summary>
        public const int SettingChanged = 20005;

        /// <summary>A setting value was removed from a level (the next level applies again).</summary>
        public const int SettingReset = 20006;

        /// <summary>A branding image is not a PNG, JPEG or WebP file (400).</summary>
        public const int BrandingImageInvalid = 20007;

        /// <summary>A branding image is larger than allowed (logo 512 KB, background 2 MB) (400).</summary>
        public const int BrandingImageTooLarge = 20008;

        /// <summary>The tenant has no image of this kind (404).</summary>
        public const int BrandingAssetNotFound = 20009;

        /// <summary>A branding image (logo, login background) was uploaded.</summary>
        public const int BrandingAssetChanged = 20010;

        /// <summary>A branding image was removed.</summary>
        public const int BrandingAssetRemoved = 20011;

        /// <summary>A custom field definition was added.</summary>
        public const int CustomFieldCreated = 20012;

        /// <summary>A custom field definition was changed.</summary>
        public const int CustomFieldUpdated = 20013;

        /// <summary>A custom field definition was removed (values already stored stay in the records).</summary>
        public const int CustomFieldDeleted = 20014;

        /// <summary>A custom field definition is not valid (key, label, type, options, group colour…) (400).</summary>
        public const int CustomFieldInvalid = 20015;

        /// <summary>The entity already has a custom field with this key (409).</summary>
        public const int CustomFieldKeyTaken = 20016;

        /// <summary>No custom field definition with this id (404).</summary>
        public const int CustomFieldNotFound = 20017;

        /// <summary>The custom field values of a record do not match the definitions of its entity (400).</summary>
        public const int CustomFieldValuesInvalid = 20018;

        /// <summary>The column layout of a grid was set for a role.</summary>
        public const int GridLayoutChanged = 20019;

        /// <summary>The column layout of a grid was removed for a role (the default applies again).</summary>
        public const int GridLayoutReset = 20020;

        /// <summary>No grid with this key, or the role does not see it (404).</summary>
        public const int GridNotFound = 20021;

        /// <summary>A grid layout names unknown columns, repeats one or hides a column that must stay visible (400).</summary>
        public const int GridLayoutInvalid = 20022;

        /// <summary>A user saved or changed a personal view of a grid (F21).</summary>
        public const int GridViewSaved = 20023;

        /// <summary>A user deleted a personal view of a grid.</summary>
        public const int GridViewDeleted = 20024;

        /// <summary>The view is not valid: name, columns, filters or sort of the grid (400).</summary>
        public const int GridViewInvalid = 20025;

        /// <summary>No view of the caller with this id on this grid (404).</summary>
        public const int GridViewNotFound = 20026;
    }
}
