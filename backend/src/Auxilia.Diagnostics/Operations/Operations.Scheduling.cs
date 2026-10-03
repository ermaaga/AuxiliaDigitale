namespace Auxilia.Diagnostics;

public static partial class Operations
{
    public static class Scheduling
    {
        public static readonly OperationDescriptor ScheduleAppointment = new("Scheduling.ScheduleAppointment", EventCodes.Scheduling.AppointmentScheduled);

        public static readonly OperationDescriptor RequestAppointment = new("Scheduling.RequestAppointment", EventCodes.Scheduling.AppointmentRequested);

        public static readonly OperationDescriptor UpdateAppointment = new("Scheduling.UpdateAppointment", EventCodes.Scheduling.AppointmentUpdated);

        public static readonly OperationDescriptor ApproveAppointment = new("Scheduling.ApproveAppointment", EventCodes.Scheduling.AppointmentApproved);

        public static readonly OperationDescriptor RejectAppointment = new("Scheduling.RejectAppointment", EventCodes.Scheduling.AppointmentRejected);

        public static readonly OperationDescriptor CompleteAppointment = new("Scheduling.CompleteAppointment", EventCodes.Scheduling.AppointmentCompleted);

        public static readonly OperationDescriptor CancelAppointment = new("Scheduling.CancelAppointment", EventCodes.Scheduling.AppointmentCancelled);

        public static readonly OperationDescriptor DeleteAppointment = new("Scheduling.DeleteAppointment", EventCodes.Scheduling.AppointmentDeleted);
    }
}
