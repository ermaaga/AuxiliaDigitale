using Auxilia.Diagnostics;
using Auxilia.Domain.Configuration;

namespace Auxilia.Domain.Tests.Configuration;

public sealed class CustomizationDomainTests
{
    private static CustomFieldSpec Spec(
        CustomFieldType type = CustomFieldType.Boolean,
        string label = "CAF",
        IReadOnlyList<string>? options = null,
        string? group = null,
        string? color = null,
        bool counter = false,
        int order = 0) =>
        new(label, type, options, IsRequired: false, group, color, VisibleOnGrid: true, counter, order);

    private static string? FieldOf(Auxilia.SharedKernel.Results.Result result) => result.Error?.ValidationErrors?.Keys.Single();

    [Fact]
    public void CustomField_LegacyBooleanWithGroupColourAndCounter()
    {
        var field = CustomFieldDefinition.Create(Guid.CreateVersion7(), "client", "CAF", Spec(group: " Area ", color: "#72fa29", counter: true)).Value;

        (field.Key, field.Label, field.GroupName, field.BadgeColor, field.DashboardCounter).ShouldBe(("CAF", "CAF", "Area", "#72fa29", true));
        field.Options.ShouldBeEmpty();
        field.HasOptions.ShouldBeFalse();
    }

    [Theory]
    [InlineData("1abc")]
    [InlineData("a-b")]
    [InlineData("")]
    [InlineData("a b")]
    public void CustomField_InvalidKey_IsRefused(string key) =>
        FieldOf(CustomFieldDefinition.Create(Guid.CreateVersion7(), "client", key, Spec())).ShouldBe("key");

    [Fact]
    public void CustomField_Rules()
    {
        static Auxilia.SharedKernel.Results.Result Create(CustomFieldSpec spec) => CustomFieldDefinition.Create(Guid.CreateVersion7(), "client", "field", spec);

        FieldOf(Create(Spec(label: " "))).ShouldBe("label");
        FieldOf(Create(Spec(label: new string('x', 101)))).ShouldBe("label");
        FieldOf(Create(Spec(type: CustomFieldType.Select))).ShouldBe("options");
        FieldOf(Create(Spec(type: CustomFieldType.Select, options: ["a", "a"]))).ShouldBe("options");
        FieldOf(Create(Spec(type: CustomFieldType.Select, options: ["a", " "]))).ShouldBe("options");
        FieldOf(Create(Spec(type: CustomFieldType.Text, options: ["a"]))).ShouldBe("options");
        FieldOf(Create(Spec(color: "#fff"))).ShouldBe("badgeColor");
        FieldOf(Create(Spec(group: "Area", color: "red"))).ShouldBe("badgeColor");
        FieldOf(Create(Spec(type: CustomFieldType.Text, counter: true))).ShouldBe("dashboardCounter");
        FieldOf(Create(Spec(order: -1))).ShouldBe("order");
        FieldOf(Create(Spec(group: new string('g', 51)))).ShouldBe("groupName");
        FieldOf(Create(Spec((CustomFieldType)99))).ShouldBe("type");
        Create(Spec(type: CustomFieldType.MultiSelect, options: [" Uno ", "Due"])).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void CustomField_Update_KeepsKeyAndType()
    {
        var field = CustomFieldDefinition.Create(Guid.CreateVersion7(), "case", "priority", Spec(CustomFieldType.Select, "Priorità", ["Alta", "Bassa"])).Value;

        field.Update(Spec(CustomFieldType.Select, "Urgenza", [" Alta ", "Media", "Bassa"], order: 3)).IsSuccess.ShouldBeTrue();
        (field.Label, field.Order).ShouldBe(("Urgenza", 3));
        field.Options.ShouldBe(["Alta", "Media", "Bassa"]);

        field.Update(Spec(CustomFieldType.Text, "Urgenza")).Error!.Code.ShouldBe(EventCodes.Configuration.CustomFieldInvalid);
        field.Label.ShouldBe("Urgenza");
    }

    [Fact]
    public void GridLayout_NeedsUniqueColumnsAndOneVisible()
    {
        var layout = GridLayout.Create(Guid.CreateVersion7(), "identity.loginAttempts", "Administrator", [new("a", true), new("b", false)]).Value;
        layout.Columns.Select(column => column.Key).ShouldBe(["a", "b"]);

        layout.Replace([new("a", true), new("a", false)]).Error!.Code.ShouldBe(EventCodes.Configuration.GridLayoutInvalid);
        layout.Replace([]).Error!.Code.ShouldBe(EventCodes.Configuration.GridLayoutInvalid);
        layout.Replace([new("a", false)]).Error!.ValidationErrors!["columns"].ShouldBe(["validation.grids.noneVisible"]);
        layout.Replace([new(new string('k', 51), true)]).IsFailure.ShouldBeTrue();
        layout.Columns.Count.ShouldBe(2);

        layout.Replace([new("b", true)]).IsSuccess.ShouldBeTrue();
        layout.Columns.ShouldHaveSingleItem().Key.ShouldBe("b");
    }
}
