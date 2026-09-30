namespace Auxilia.Diagnostics;

public static partial class Operations
{
    public static class Configuration
    {
        public static readonly OperationDescriptor SetSetting = new("Configuration.SetSetting", EventCodes.Configuration.SettingChanged);

        public static readonly OperationDescriptor ResetSetting = new("Configuration.ResetSetting", EventCodes.Configuration.SettingReset);
    }
}
