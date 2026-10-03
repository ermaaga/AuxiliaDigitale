using Auxilia.Diagnostics;
using Auxilia.Domain.Cases;

using Case = Auxilia.Domain.Cases.Case;

namespace Auxilia.Domain.Tests.Cases;

public sealed class CaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.CreateVersion7();

    private static CaseOpening Opening(DateOnly? startedOn = null, DateOnly? dueOn = null) =>
        new("2026-00001", Guid.CreateVersion7(), Guid.CreateVersion7(), 150m, "EUR", null, startedOn ?? new DateOnly(2026, 10, 1), dueOn, "{}");

    private static Case Open() => Case.Open(Guid.CreateVersion7(), Opening(), Actor, Now).Value;

    private static Case Sent()
    {
        var @case = Open();
        @case.Advance(Actor, null, Now).IsSuccess.ShouldBeTrue();
        @case.Advance(Actor, null, Now).IsSuccess.ShouldBeTrue();
        return @case;
    }

    [Fact]
    public void Open_StartsInserted_WithTheOpeningInTheHistory()
    {
        var @case = Open();

        (@case.Status, @case.IsActive, @case.Price, @case.AmountPaid, @case.ExpiresOn).ShouldBe((CaseStatus.Inserted, true, 150m, 0m, (DateOnly?)null));
        var opening = @case.History.ShouldHaveSingleItem();
        (opening.FromStatus, opening.ToStatus, opening.ChangedByUserId).ShouldBe(((CaseStatus?)null, CaseStatus.Inserted, (Guid?)Actor));
    }

    [Fact]
    public void Open_DatesAreChecked()
    {
        var result = Case.Open(Guid.CreateVersion7(), Opening(new DateOnly(1800, 1, 1), new DateOnly(1700, 1, 1)), Actor, Now);

        result.Error!.Code.ShouldBe(EventCodes.Cases.CaseInvalid);
        result.Error.ValidationErrors.Keys.ShouldBe(["startedOn", "dueOn"], ignoreOrder: true);
    }

    [Fact]
    public void Advance_AndGoBack_OneStepAtATime()
    {
        var @case = Open();

        @case.GoBack(Actor, null, Now).Error!.Code.ShouldBe(EventCodes.Cases.CaseCannotGoBack);
        @case.Advance(Actor, "documents arrived", Now).IsSuccess.ShouldBeTrue();
        @case.Advance(Actor, null, Now).IsSuccess.ShouldBeTrue();
        @case.Status.ShouldBe(CaseStatus.Sent);
        @case.Advance(Actor, null, Now).Error!.Code.ShouldBe(EventCodes.Cases.CaseCompletionRequired);
        @case.GoBack(Actor, null, Now).IsSuccess.ShouldBeTrue();
        @case.GoBack(Actor, null, Now).IsSuccess.ShouldBeTrue();

        @case.Status.ShouldBe(CaseStatus.Inserted);
        @case.History.Select(change => (change.FromStatus, change.ToStatus)).ShouldBe(
        [
            (null, CaseStatus.Inserted),
            (CaseStatus.Inserted, CaseStatus.InProgress),
            (CaseStatus.InProgress, CaseStatus.Sent),
            (CaseStatus.Sent, CaseStatus.InProgress),
            (CaseStatus.InProgress, CaseStatus.Inserted),
        ]);
        @case.History[1].Note.ShouldBe("documents arrived");
    }

    [Fact]
    public void Complete_OnlyFromSent_RecordsThePayment_AndIsTerminal()
    {
        Open().Complete(10m, false, Actor, null, Now).Error!.ValidationErrors.Keys.ShouldBe(["status"]);
        var @case = Sent();

        @case.Complete(120.5m, rejected: true, Actor, "  done  ", Now).IsSuccess.ShouldBeTrue();

        (@case.Status, @case.IsRejected, @case.ExpiresOn, @case.CompletedAt, @case.AmountPaid)
            .ShouldBe((CaseStatus.Completed, true, (DateOnly?)new DateOnly(2026, 10, 3), (DateTimeOffset?)Now, 120.5m));
        (@case.Payments.ShouldHaveSingleItem().Note, @case.History[^1].Note).ShouldBe(("done", "done"));
        @case.Advance(Actor, null, Now).Error!.Code.ShouldBe(EventCodes.Cases.CaseIsCompleted);
        @case.GoBack(Actor, null, Now).Error!.Code.ShouldBe(EventCodes.Cases.CaseIsCompleted);
        @case.Complete(1m, false, Actor, null, Now).Error!.Code.ShouldBe(EventCodes.Cases.CaseIsCompleted);
        @case.AddPayment(1m, new DateOnly(2026, 10, 3), null, Actor, Now).Error!.Code.ShouldBe(EventCodes.Cases.CaseIsCompleted);
        @case.Update(null, "{}").Error!.Code.ShouldBe(EventCodes.Cases.CaseIsCompleted);
    }

    [Fact]
    public void Complete_WithZero_RecordsNoPayment_AndInvalidAmountsAreRefused()
    {
        var @case = Sent();

        @case.Complete(-1m, false, Actor, null, Now).Error!.ValidationErrors.Keys.ShouldBe(["amountPaid"]);
        @case.Complete(1.001m, false, Actor, null, Now).IsFailure.ShouldBeTrue();
        @case.Complete(0m, false, Actor, null, Now).IsSuccess.ShouldBeTrue();
        @case.Payments.ShouldBeEmpty();
    }

    [Fact]
    public void AddPayment_SumsUp_AndChecksAmountDateAndNote()
    {
        var @case = Open();

        @case.AddPayment(50m, new DateOnly(2026, 10, 2), " acconto ", Actor, Now).IsSuccess.ShouldBeTrue();
        @case.AddPayment(25.25m, new DateOnly(2026, 10, 3), null, Actor, Now).IsSuccess.ShouldBeTrue();

        @case.AmountPaid.ShouldBe(75.25m);
        @case.Payments[0].Note.ShouldBe("acconto");
        @case.AddPayment(0m, new DateOnly(2027, 1, 1), new string('x', Case.NoteMaxLength + 1), Actor, Now).Error!.ValidationErrors.Keys
            .ShouldBe(["amount", "paidOn", "note"], ignoreOrder: true);
    }

    [Fact]
    public void Update_ChangesDueDateAndCustomFields()
    {
        var @case = Open();

        @case.Update(new DateOnly(2026, 12, 31), "{\"urgent\":true}").IsSuccess.ShouldBeTrue();
        (@case.DueOn, @case.CustomFields).ShouldBe(((DateOnly?)new DateOnly(2026, 12, 31), "{\"urgent\":true}"));
        @case.Update(new DateOnly(2026, 1, 1), "{}").Error!.ValidationErrors.Keys.ShouldBe(["dueOn"]);
        @case.Advance(Actor, new string('x', Case.NoteMaxLength + 1), Now).Error!.ValidationErrors.Keys.ShouldBe(["note"]);
        @case.Status.ShouldBe(CaseStatus.Inserted);
    }

    [Theory]
    [InlineData(true, CaseStatus.Inserted, null, true)]
    [InlineData(true, CaseStatus.Sent, "2026-10-03", true)]
    [InlineData(true, CaseStatus.Sent, "2026-10-02", false)]
    [InlineData(true, CaseStatus.Completed, null, false)]
    [InlineData(false, CaseStatus.InProgress, null, false)]
    public void CountsAsOpen_FollowsTheLegacyRule(bool isActive, CaseStatus status, string? expiresOn, bool open)
    {
        Case.CountsAsOpen(isActive, status, expiresOn is null ? null : DateOnly.Parse(expiresOn, System.Globalization.CultureInfo.InvariantCulture), new DateOnly(2026, 10, 3))
            .ShouldBe(open);
    }
}
