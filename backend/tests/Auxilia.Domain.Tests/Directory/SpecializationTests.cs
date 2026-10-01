using Auxilia.Diagnostics;
using Auxilia.Domain.Directory;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Domain.Tests.Directory;

public sealed class SpecializationTests
{
    private static SpecializationSpec Spec(string name = "Fisioterapia", string? email = null, string? phone = null, string? description = null, bool isPrivate = false) =>
        new(name, description, email, phone, isPrivate);

    private static string? FieldOf(Result result) => result.Error?.ValidationErrors?.Keys.Single();

    [Fact]
    public void Create_TrimsAndKeepsTheLegacyFields()
    {
        var specialization = Specialization.Create(
            Guid.CreateVersion7(), TenantRole.Employee, Spec(" Fisioterapia ", " info@example.com ", "+39 06 1234-567", "  ", isPrivate: true)).Value;

        (specialization.Name, specialization.Email, specialization.WorkPhone, specialization.Description).ShouldBe(("Fisioterapia", "info@example.com", "+39 06 1234-567", null));
        specialization.IsPrivate.ShouldBeTrue();
        specialization.IsActive.ShouldBeTrue();
        specialization.Role.ShouldBe(TenantRole.Employee);
    }

    [Fact]
    public void Create_OnlyClientAndEmployee()
    {
        var refused = Specialization.Create(Guid.CreateVersion7(), TenantRole.Administrator, Spec());

        refused.Error!.Code.ShouldBe(EventCodes.Directory.SpecializationInvalid);
        FieldOf(refused).ShouldBe("role");
        Specialization.Create(Guid.CreateVersion7(), TenantRole.Client, Spec()).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Update_ValidatesEveryField()
    {
        var specialization = Specialization.Create(Guid.CreateVersion7(), TenantRole.Client, Spec()).Value;

        FieldOf(specialization.Update(Spec(name: " "))).ShouldBe("name");
        FieldOf(specialization.Update(Spec(name: new string('a', Specialization.NameMaxLength + 1)))).ShouldBe("name");
        FieldOf(specialization.Update(Spec(email: "not-an-email"))).ShouldBe("email");
        FieldOf(specialization.Update(Spec(phone: "call me"))).ShouldBe("workPhone");
        FieldOf(specialization.Update(Spec(description: new string('a', Specialization.DescriptionMaxLength + 1)))).ShouldBe("description");
        specialization.Name.ShouldBe("Fisioterapia");

        specialization.Update(Spec("Nutrizione", isPrivate: true)).IsSuccess.ShouldBeTrue();
        (specialization.Name, specialization.IsPrivate).ShouldBe(("Nutrizione", true));
    }

    [Fact]
    public void Members_AreAddedOnceAndRemoved()
    {
        var specialization = Specialization.Create(Guid.CreateVersion7(), TenantRole.Employee, Spec()).Value;
        var (anna, luca) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var at = DateTimeOffset.UtcNow;

        specialization.AddMembers([anna, luca, anna], at).ShouldBe(2);
        specialization.AddMembers([anna], at).ShouldBe(0);
        specialization.Members.Select(member => member.UserId).ShouldBe([anna, luca], ignoreOrder: true);
        specialization.Members.ShouldAllBe(member => member.SpecializationId == specialization.Id && member.AssignedAt == at);

        specialization.RemoveMember(anna).ShouldBeTrue();
        specialization.RemoveMember(anna).ShouldBeFalse();
        specialization.Members.ShouldHaveSingleItem().UserId.ShouldBe(luca);

        specialization.Deactivate();
        specialization.IsActive.ShouldBeFalse();
        specialization.Members.Count.ShouldBe(1);
    }
}
