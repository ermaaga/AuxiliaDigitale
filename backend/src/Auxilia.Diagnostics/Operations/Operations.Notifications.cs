namespace Auxilia.Diagnostics;

public static partial class Operations
{
    public static class Notifications
    {
        public static readonly OperationDescriptor SendNotifications = new("Notifications.SendNotifications", EventCodes.Notifications.NotificationsSent);

        public static readonly OperationDescriptor MarkNotificationRead = new("Notifications.MarkNotificationRead", EventCodes.Notifications.NotificationRead);

        public static readonly OperationDescriptor MarkAllNotificationsRead = new("Notifications.MarkAllNotificationsRead", EventCodes.Notifications.AllNotificationsRead);

        public static readonly OperationDescriptor DeleteNotification = new("Notifications.DeleteNotification", EventCodes.Notifications.NotificationDeleted);

        public static readonly OperationDescriptor SaveNotificationPreferences = new("Notifications.SaveNotificationPreferences", EventCodes.Notifications.NotificationPreferencesSaved);
    }
}
