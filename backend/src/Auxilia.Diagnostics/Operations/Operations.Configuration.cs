namespace Auxilia.Diagnostics;

public static partial class Operations
{
    public static class Configuration
    {
        public static readonly OperationDescriptor SetSetting = new("Configuration.SetSetting", EventCodes.Configuration.SettingChanged);

        public static readonly OperationDescriptor ResetSetting = new("Configuration.ResetSetting", EventCodes.Configuration.SettingReset);

        public static readonly OperationDescriptor SetBrandingAsset = new("Configuration.SetBrandingAsset", EventCodes.Configuration.BrandingAssetChanged);

        public static readonly OperationDescriptor CreateCustomField = new("Configuration.CreateCustomField", EventCodes.Configuration.CustomFieldCreated);

        public static readonly OperationDescriptor UpdateCustomField = new("Configuration.UpdateCustomField", EventCodes.Configuration.CustomFieldUpdated);

        public static readonly OperationDescriptor DeleteCustomField = new("Configuration.DeleteCustomField", EventCodes.Configuration.CustomFieldDeleted);

        public static readonly OperationDescriptor SetGridLayout = new("Configuration.SetGridLayout", EventCodes.Configuration.GridLayoutChanged);

        public static readonly OperationDescriptor ResetGridLayout = new("Configuration.ResetGridLayout", EventCodes.Configuration.GridLayoutReset);

        public static readonly OperationDescriptor RemoveBrandingAsset = new("Configuration.RemoveBrandingAsset", EventCodes.Configuration.BrandingAssetRemoved);
    }
}
