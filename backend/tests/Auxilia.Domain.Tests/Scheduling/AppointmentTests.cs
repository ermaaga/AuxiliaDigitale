using Auxilia.Diagnostics;
using Auxilia.Domain.Scheduling;

namespace Auxilia.Domain.Tests.Scheduling;

public sealed class AppointmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.CreateVersion7();

    private static AppointmentSlot Slot(DateTimeOffset? startsAt = null, int duration = 60, string? notes = " note ", bool global = true) =>
        new(startsAt ?? Now.AddDays(1), duration, notes, global, "{}");

    private static Appointment Scheduled() => Appointment.Schedule(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Slot(), Actor, Now).Value;

    private static Appointment Requested() => Appointment.Request(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Slot(), Actor, Now).Value;

    [Fact]
    public void Schedule_IsApproved_WithTheCreationInTheHistory()
    {
        var appointment = Scheduled();

        (appointment.Status, appointment.RequestedByClient, appointment.Notes, appointment.EndsAt).ShouldBe(
            (AppointmentStatus.Approved, false, "note", Now.AddDays(1).AddHours(1)));
        var creation = appointment.History.ShouldHaveSingleItem();
        (creation.FromStatus, creation.ToStatus, creation.ChangedByUserId).ShouldBe(((AppointmentStatus?)null, AppointmentStatus.Approved, (Guid?)Actor));
    }

    [Fact]
    public void Request_IsPending_AndRequestedByTheClient()
    {
        var appointment = Requested();

        (appointment.Status, appointment.RequestedByClient).ShouldBe((AppointmentStatus.Pending, true));
    }

    [Fact]
    public void Create_RefusesThePast_ABadDuration_AndLongNotes()
    {
        var result = Appointment.Schedule(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Slot(Now, 4, new string('x', 1001)), Actor, Now);

        result.Error!.Code.ShouldBe(EventCodes.Scheduling.AppointmentInvalid);
        result.Error.ValidationErrors!.Keys.ShouldBe(["startsAt", "durationMinutes", "notes"], ignoreOrder: true);
        Appointment.Request(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Slot(duration: 1441), Actor, Now)
            .Error!.ValidationErrors!.Keys.ShouldBe(["durationMinutes"]);
    }

    [Fact]
    public void Update_MovesAnOpenAppointment_AndChecksOnlyANewStart()
    {
        var appointment = Scheduled();
        var later = Now.AddDays(2);

        appointment.Update(Slot(later, 30, null, global: false), Now).IsSuccess.ShouldBeTrue();
        (appointment.StartsAt, appointment.EndsAt, appointment.Notes, appointment.ShowInGlobalCalendar).ShouldBe((later, later.AddMinutes(30), (string?)null, false));

        // Once begun, the same start may stay (e.g. the notes change); a new one must be in the future.
        appointment.Update(Slot(later, 45), later.AddMinutes(5)).IsSuccess.ShouldBeTrue();
        appointment.Update(Slot(later.AddMinutes(1)), later.AddMinutes(5)).Error!.ValidationErrors!.Keys.ShouldBe(["startsAt"]);
    }

    [Fact]
    public void PendingAppointments_AreApprovedOrRejected_Once()
    {
        var approved = Requested();
        approved.Approve(Actor, "ok", Now).IsSuccess.ShouldBeTrue();
        approved.Status.ShouldBe(AppointmentStatus.Approved);
        approved.History[^1].Note.ShouldBe("ok");
        approved.Approve(Actor, null, Now).Error!.Code.ShouldBe(EventCodes.Scheduling.AppointmentNotPending);
        approved.Reject(Actor, null, Now).Error!.Code.ShouldBe(EventCodes.Scheduling.AppointmentNotPending);

        var rejected = Requested();
        rejected.Reject(Actor, null, Now).IsSuccess.ShouldBeTrue();
        rejected.Status.ShouldBe(AppointmentStatus.Rejected);
        rejected.Update(Slot(), Now).Error!.Code.ShouldBe(EventCodes.Scheduling.AppointmentClosed);
        rejected.Cancel(Actor, null, Now).Error!.Code.ShouldBe(EventCodes.Scheduling.AppointmentClosed);
    }

    [Fact]
    public void ApprovedAppointments_AreCompleted_PendingOnesAreNot()
    {
        Requested().Complete(Actor, null, Now).Error!.Code.ShouldBe(EventCodes.Scheduling.AppointmentNotApproved);

        var appointment = Scheduled();
        appointment.Complete(Actor, null, Now).IsSuccess.ShouldBeTrue();
        appointment.Status.ShouldBe(AppointmentStatus.Completed);
        appointment.History.Select(change => change.Sequence).ShouldBe([1, 2]);
        appointment.Complete(Actor, null, Now).Error!.Code.ShouldBe(EventCodes.Scheduling.AppointmentNotApproved);
    }

    [Fact]
    public void Cancel_ClosesPendingAndApproved_AndChecksTheNote()
    {
        var pending = Requested();
        pending.Cancel(Actor, new string('x', 501), Now).Error!.ValidationErrors!.Keys.ShouldBe(["note"]);
        pending.Cancel(Actor, null, Now).IsSuccess.ShouldBeTrue();
        pending.Status.ShouldBe(AppointmentStatus.Cancelled);

        var approved = Scheduled();
        approved.Cancel(Actor, null, Now).IsSuccess.ShouldBeTrue();
        approved.History[^1].FromStatus.ShouldBe(AppointmentStatus.Approved);
    }

    [Fact]
    public void IsOpen_PendingAndApprovedOnly()
    {
        Enum.GetValues<AppointmentStatus>().Where(Appointment.IsOpen).ShouldBe([AppointmentStatus.Pending, AppointmentStatus.Approved]);
    }

    [Fact]
    public void ImportLegacy_AcceptsPastAppointments_WithOneHistoryRow()
    {
        var imported = Appointment.ImportLegacy(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Slot(Now.AddYears(-1)), AppointmentStatus.Completed, requestedByClient: false, Now.AddYears(-1).AddDays(-3)).Value;

        (imported.Status, imported.StartsAt, imported.EndsAt).ShouldBe((AppointmentStatus.Completed, Now.AddYears(-1), Now.AddYears(-1).AddMinutes(60)));
        imported.History.Select(change => (change.FromStatus, change.ToStatus, change.ChangedByUserId)).ShouldBe([((AppointmentStatus?)null, AppointmentStatus.Completed, (Guid?)null)]);
        Appointment.ImportLegacy(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Slot(duration: 1), AppointmentStatus.Pending, true, Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void ApplyLegacyState_FollowsTheLegacyTimeAndStatus()
    {
        var imported = Appointment.ImportLegacy(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Slot(Now.AddDays(2)), AppointmentStatus.Pending, true, Now).Value;

        imported.ApplyLegacyState(Slot(Now.AddDays(2)), AppointmentStatus.Pending, Now).ShouldBeFalse();
        imported.ApplyLegacyState(Slot(Now.AddDays(3), duration: 30), AppointmentStatus.Approved, Now).ShouldBeTrue();

        (imported.StartsAt, imported.EndsAt, imported.Status).ShouldBe((Now.AddDays(3), Now.AddDays(3).AddMinutes(30), AppointmentStatus.Approved));
        imported.History.Select(change => change.ToStatus).ShouldBe([AppointmentStatus.Pending, AppointmentStatus.Approved]);
    }
}
