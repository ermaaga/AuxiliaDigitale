namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(17000, "Engagement: requests, tasks, activities")]
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

        /// <summary>A task was created (B-26).</summary>
        public const int TaskCreated = 17008;

        /// <summary>A task was changed (title, notes, due date, assignee, links).</summary>
        public const int TaskUpdated = 17009;

        /// <summary>A task was done.</summary>
        public const int TaskCompleted = 17010;

        /// <summary>A done task was opened again.</summary>
        public const int TaskReopened = 17011;

        /// <summary>A task was deleted (soft).</summary>
        public const int TaskDeleted = 17012;

        /// <summary>A value of a task is not valid (400, field errors).</summary>
        public const int TaskInvalid = 17013;

        /// <summary>No task with this id visible to the caller (404).</summary>
        public const int TaskNotFound = 17014;

        /// <summary>An activity (note, call, meeting, e-mail) was written on a client (B-26).</summary>
        public const int ActivityAdded = 17015;

        /// <summary>An activity of a client was deleted.</summary>
        public const int ActivityDeleted = 17016;

        /// <summary>A value of an activity is not valid (400, field errors).</summary>
        public const int ActivityInvalid = 17017;

        /// <summary>No activity with this id visible to the caller (404).</summary>
        public const int ActivityNotFound = 17018;
    }
}
