using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Scheduling
    {
        public static Error AppointmentInvalid(IReadOnlyDictionary<string, string[]> errors) =>
            Error.Validation(EventCodes.Scheduling.AppointmentInvalid, "The appointment is not valid", errors);

        public static Error AppointmentInvalid(string field, string messageKey) =>
            AppointmentInvalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [messageKey] });

        public static Error AppointmentNotFound() =>
            Error.NotFound(EventCodes.Scheduling.AppointmentNotFound, "Appointment not found");

        public static Error AppointmentClosed() =>
            Error.Conflict(EventCodes.Scheduling.AppointmentClosed, "The appointment is rejected, completed or cancelled and cannot change any more");

        public static Error AppointmentNotPending() =>
            Error.Conflict(EventCodes.Scheduling.AppointmentNotPending, "Only a pending appointment can be approved or rejected");

        public static Error AppointmentNotApproved() =>
            Error.Conflict(EventCodes.Scheduling.AppointmentNotApproved, "Only an approved appointment can be completed");

        public static Error AppointmentClientNotInCharge() =>
            Error.Forbidden(EventCodes.Scheduling.AppointmentClientNotInCharge, "Employees schedule appointments only for the clients in their charge");
    }
}
