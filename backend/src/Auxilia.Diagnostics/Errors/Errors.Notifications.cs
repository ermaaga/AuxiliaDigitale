using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Notifications
    {
        public static Error NotificationNotFound() =>
            Error.NotFound(EventCodes.Notifications.NotificationNotFound, "Notification not found");

        public static Error NotificationPreferencesInvalid(IReadOnlyDictionary<string, string[]> errors) =>
            Error.Validation(EventCodes.Notifications.NotificationPreferencesInvalid, "The notification preferences are not valid", errors);
    }
}
