namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(15000, "Scheduling")]
    public static class Scheduling
    {
        /// <summary>Staff scheduled an appointment for a client (F13): already approved, the client is told.</summary>
        public const int AppointmentScheduled = 15001;

        /// <summary>A client requested an appointment with an employee (Pending): the employee is told.</summary>
        public const int AppointmentRequested = 15002;

        /// <summary>An open appointment was moved or edited (date, time, duration, notes, global flag, custom fields).</summary>
        public const int AppointmentUpdated = 15003;

        /// <summary>A pending appointment was approved.</summary>
        public const int AppointmentApproved = 15004;

        /// <summary>A pending appointment was rejected.</summary>
        public const int AppointmentRejected = 15005;

        /// <summary>An approved appointment was completed.</summary>
        public const int AppointmentCompleted = 15006;

        /// <summary>A pending or approved appointment was cancelled (Q21: also by the client).</summary>
        public const int AppointmentCancelled = 15007;

        /// <summary>An appointment was deleted by staff (soft delete, history kept).</summary>
        public const int AppointmentDeleted = 15008;

        /// <summary>A value of an appointment is not valid (400, field errors; past start Q20).</summary>
        public const int AppointmentInvalid = 15009;

        /// <summary>No appointment with this id visible to the caller (404).</summary>
        public const int AppointmentNotFound = 15010;

        /// <summary>A rejected, completed or cancelled appointment cannot change any more (409).</summary>
        public const int AppointmentClosed = 15011;

        /// <summary>Only a pending appointment is approved or rejected (409).</summary>
        public const int AppointmentNotPending = 15012;

        /// <summary>Only an approved appointment is completed (409).</summary>
        public const int AppointmentNotApproved = 15013;

        /// <summary>An employee schedules appointments only for the clients in charge (403, legacy "my clients").</summary>
        public const int AppointmentClientNotInCharge = 15014;
    }
}
