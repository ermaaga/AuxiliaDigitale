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
    }
}
