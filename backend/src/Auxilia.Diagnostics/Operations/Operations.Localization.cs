namespace Auxilia.Diagnostics;

public static partial class Operations
{
    public static class Localization
    {
        public static readonly OperationDescriptor CreateKey = new("Localization.CreateKey", EventCodes.Localization.ResourceKeyCreated);

        public static readonly OperationDescriptor UpdateKey = new("Localization.UpdateKey", EventCodes.Localization.ResourceKeyUpdated);

        public static readonly OperationDescriptor DeleteKey = new("Localization.DeleteKey", EventCodes.Localization.ResourceKeyDeleted);

        public static readonly OperationDescriptor SetTranslation = new("Localization.SetTranslation", EventCodes.Localization.TranslationSet);

        public static readonly OperationDescriptor RemoveTranslation = new("Localization.RemoveTranslation", EventCodes.Localization.TranslationRemoved);
    }
}
