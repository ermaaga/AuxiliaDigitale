using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Domain.Tests.Identity;

public sealed class PermissionTests
{
    [Fact]
    public void PermissionEntry_KeepsCodeAndModule_AndRejectsInvalidValues()
    {
        var entry = new PermissionEntry("cases.cases.view", "cases");

        (entry.Code, entry.ModuleCode).ShouldBe(("cases.cases.view", "cases"));
        Should.Throw<ArgumentException>(() => new PermissionEntry(" ", "cases"));
        Should.Throw<ArgumentException>(() => new PermissionEntry("cases.cases.view", ""));
        Should.Throw<ArgumentOutOfRangeException>(() => new PermissionEntry(new string('a', PermissionEntry.CodeMaxLength + 1), "cases"));
        Should.Throw<ArgumentOutOfRangeException>(() => new PermissionEntry("cases.cases.view", new string('a', PermissionEntry.ModuleCodeMaxLength + 1)));
    }

    [Fact]
    public void RoleGrant_KeepsRoleAndPermission()
    {
        var grant = new RoleGrant(TenantRole.Employee, "cases.cases.manage");

        (grant.Role, grant.PermissionCode).ShouldBe((TenantRole.Employee, "cases.cases.manage"));
        Should.Throw<ArgumentException>(() => new RoleGrant(TenantRole.Client, ""));
    }

    [Theory]
    [InlineData("Administrator", true)]
    [InlineData("Client", true)]
    [InlineData("client", false)]
    [InlineData("1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TenantRoles_ParseOnlyExactNames(string? value, bool parsed)
    {
        TenantRoles.TryParse(value, out var role).ShouldBe(parsed);
        if (parsed)
        {
            role.ToString().ShouldBe(value);
        }
    }
}
