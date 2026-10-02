using Auxilia.Diagnostics;
using Auxilia.Domain.Directory;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Tests.Directory;

public sealed class ClientTests
{
    private static readonly DateOnly Today = new(2026, 10, 2);
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    private static PersonDetails Details(
        string? firstName = " Mario ", string? lastName = "Rossi", string? email = "mario.rossi@example.test",
        DateOnly? birthDate = null, string? phone = "+39 333 123 4567", string? fiscalCode = "rssmra80a01h501u") =>
        new(firstName, lastName, email, birthDate ?? new DateOnly(1980, 1, 1), phone, fiscalCode);

    [Fact]
    public void Create_ValidDetails_TrimsAndUppercasesTheFiscalCode()
    {
        var person = Person.Create(Guid.CreateVersion7(), Details(), Today).Value;

        (person.FirstName, person.LastName, person.FullName).ShouldBe(("Mario", "Rossi", "Mario Rossi"));
        person.FiscalCode.ShouldBe("RSSMRA80A01H501U");
        person.Phone.ShouldBe("+39 333 123 4567");
        person.CustomFields.ShouldBe("{}");
    }

    [Theory]
    [InlineData("RSSMRA80A01H501U")]
    [InlineData("RSSMRA80A01H50MU")] // omocodia: a digit replaced by a letter
    [InlineData("RSS MRA 80A01 H501U")]
    public void Create_FiscalCode_AcceptsOmocodiaAndSpaces(string fiscalCode)
    {
        Person.Create(Guid.CreateVersion7(), Details(fiscalCode: fiscalCode), Today).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_InvalidValues_ReportsEveryFieldAtOnce()
    {
        var result = Person.Create(
            Guid.CreateVersion7(),
            new PersonDetails(" ", new string('x', Person.NameMaxLength + 1), "not-an-email", new DateOnly(2027, 1, 1), "12ab", "RSSMRA80Z01H501U"),
            Today);

        result.Error!.Code.ShouldBe(EventCodes.Directory.PersonInvalid);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.ValidationErrors.Keys.ShouldBe(["firstName", "lastName", "email", "birthDate", "phone", "fiscalCode"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("1234567")]
    [InlineData("123456789012345678901")]
    [InlineData("333-1234567x")]
    public void Create_Phone_FollowsOneRule(string phone)
    {
        Person.Create(Guid.CreateVersion7(), Details(phone: phone), Today).Error!.ValidationErrors.Keys.ShouldBe(["phone"]);
    }

    [Fact]
    public void Update_Refused_LeavesThePersonUntouched()
    {
        var person = Person.Create(Guid.CreateVersion7(), Details(), Today).Value;

        person.Update(Details(firstName: "Luigi", birthDate: new DateOnly(1890, 1, 1)), Today).IsFailure.ShouldBeTrue();

        person.FirstName.ShouldBe("Mario");
        person.Update(Details(firstName: "Luigi", email: null, phone: null), Today).IsSuccess.ShouldBeTrue();
        (person.FirstName, person.Email, person.Phone).ShouldBe(("Luigi", null, null));
    }

    [Fact]
    public void NewClient_IsInactiveUntilACaseIsOpen_AndRecordsTheEmployee()
    {
        var employee = Guid.CreateVersion7();
        var profile = ClientProfile.Create(Guid.CreateVersion7(), employee, Now);

        profile.Status.ShouldBe(ClientStatus.Inactive);
        profile.EmployeeUserId.ShouldBe(employee);
        profile.Assignments.ShouldHaveSingleItem().EndedAt.ShouldBeNull();
        profile.UpdateStatus(hasOpenCases: true, Now.AddDays(1)).ShouldBeTrue();
        (profile.Status, profile.StatusChangedAt).ShouldBe((ClientStatus.Active, Now.AddDays(1)));
        profile.UpdateStatus(hasOpenCases: true, Now.AddDays(2)).ShouldBeFalse();
        profile.UpdateStatus(hasOpenCases: false, Now.AddDays(3)).ShouldBeTrue();
        profile.Status.ShouldBe(ClientStatus.Inactive);
    }

    [Fact]
    public void Assign_EndsThePreviousAssignment_AndKeepsTheHistory()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var profile = ClientProfile.Create(Guid.CreateVersion7(), first, Now);

        profile.Assign(first, Now.AddHours(1)).ShouldBeFalse();
        profile.Assign(second, Now.AddHours(2)).ShouldBeTrue();
        profile.Unassign(Now.AddHours(3)).ShouldBeTrue();
        profile.Unassign(Now.AddHours(4)).ShouldBeFalse();

        profile.EmployeeUserId.ShouldBeNull();
        profile.Assignments.Select(item => (item.EmployeeUserId, item.AssignedAt, item.EndedAt)).ShouldBe(
        [
            (first, Now, Now.AddHours(2)),
            (second, Now.AddHours(2), Now.AddHours(3)),
        ]);
    }

    [Fact]
    public void CanEnableSignIn_NeedsAnEmployeeInCharge()
    {
        var profile = ClientProfile.Create(Guid.CreateVersion7(), null, Now);

        profile.CanEnableSignIn().Error!.Code.ShouldBe(EventCodes.Directory.ClientEmployeeRequired);
        profile.Assign(Guid.CreateVersion7(), Now);
        profile.CanEnableSignIn().IsSuccess.ShouldBeTrue();
    }
}
