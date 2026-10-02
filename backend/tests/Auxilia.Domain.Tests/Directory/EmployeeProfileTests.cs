using Auxilia.Domain.Directory;

namespace Auxilia.Domain.Tests.Directory;

public sealed class EmployeeProfileTests
{
    [Fact]
    public void Create_IsNotDefault_AndReportsToNobody()
    {
        var userId = Guid.CreateVersion7();

        var profile = EmployeeProfile.Create(userId);

        (profile.Id, profile.IsDefault, profile.AdministratorUserId).ShouldBe((userId, false, null));
    }

    [Fact]
    public void MakeAndClearDefault_ReportWhetherSomethingChanged()
    {
        var profile = EmployeeProfile.Create(Guid.CreateVersion7());

        profile.ClearDefault().ShouldBeFalse();
        profile.MakeDefault().ShouldBeTrue();
        profile.MakeDefault().ShouldBeFalse();
        profile.IsDefault.ShouldBeTrue();
        profile.ClearDefault().ShouldBeTrue();
        profile.IsDefault.ShouldBeFalse();
    }

    [Fact]
    public void SetAdministrator_ChangesOrRemovesIt()
    {
        var profile = EmployeeProfile.Create(Guid.CreateVersion7());
        var administrator = Guid.CreateVersion7();

        profile.SetAdministrator(null).ShouldBeFalse();
        profile.SetAdministrator(administrator).ShouldBeTrue();
        profile.SetAdministrator(administrator).ShouldBeFalse();
        profile.AdministratorUserId.ShouldBe(administrator);
        profile.SetAdministrator(null).ShouldBeTrue();
        profile.AdministratorUserId.ShouldBeNull();
    }
}
