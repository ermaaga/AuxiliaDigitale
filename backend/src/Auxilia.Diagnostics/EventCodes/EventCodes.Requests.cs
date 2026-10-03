namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(17000, "Engagement: requests")]
    public static class Requests
    {
        /// <summary>A request was sent (F15): to the employee in charge or to the office; the recipients are told.</summary>
        public const int RequestCreated = 17001;

        /// <summary>A message was added to a request thread; the other party is told (Q17: always).</summary>
        public const int RequestReplied = 17002;

        /// <summary>A request was closed.</summary>
        public const int RequestClosed = 17003;

        /// <summary>A request was deleted by an Administrator (soft delete, thread kept).</summary>
        public const int RequestDeleted = 17004;

        /// <summary>A value of a request is not valid (400, field errors).</summary>
        public const int RequestInvalid = 17005;

        /// <summary>No request with this id visible to the caller (404).</summary>
        public const int RequestNotFound = 17006;

        /// <summary>A closed request takes no more messages and cannot be closed again (409).</summary>
        public const int RequestIsClosed = 17007;
    }
}
