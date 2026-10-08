using Auxilia.Diagnostics;
using Auxilia.Domain.Configuration;

namespace Auxilia.Domain.Tests.Configuration;

public sealed class GridViewTests
{
    private static readonly Guid User = Guid.CreateVersion7();

    private static GridViewSpec Spec(
        string name = "Mine",
        IReadOnlyList<string>? hidden = null,
        IReadOnlyDictionary<string, string>? filters = null,
        string? sort = "-startedOn") =>
        new(name, hidden ?? ["email"], filters ?? new Dictionary<string, string> { ["status"] = "Active" }, sort);

    [Fact]
    public void Create_TrimsAndKeepsTheSpec_NotDefaultYet()
    {
        var view = GridView.Create(Guid.CreateVersion7(), User, "cases.cases",
            Spec(" Mine ", ["email", "email", "phone"], new Dictionary<string, string> { ["status"] = " Active " }, "  ")).Value;

        (view.UserId, view.GridKey, view.Name, view.Sort, view.IsDefault).ShouldBe((User, "cases.cases", "Mine", null, false));
        view.HiddenColumns.ShouldBe(["email", "phone"]);
        view.Filters.ShouldBe(new Dictionary<string, string> { ["status"] = "Active" });
    }

    [Theory]
    [InlineData("", "name")]
    [InlineData("   ", "name")]
    public void Change_RefusesAnEmptyName(string name, string field)
    {
        var view = GridView.Create(Guid.CreateVersion7(), User, "cases.cases", Spec()).Value;

        var changed = view.Change(Spec(name));

        changed.Error!.Code.ShouldBe(EventCodes.Configuration.GridViewInvalid);
        changed.Error.ValidationErrors.Keys.ShouldBe([field]);
        view.Name.ShouldBe("Mine");
    }

    [Fact]
    public void Change_RefusesTooLongValues_AndEmptyFilters()
    {
        var created = GridView.Create(Guid.CreateVersion7(), User, "cases.cases", Spec(
            new string('n', GridView.NameMaxLength + 1),
            [new string('c', GridLayout.ColumnKeyMaxLength + 1)],
            new Dictionary<string, string> { ["status"] = " " },
            new string('s', GridView.SortMaxLength + 1)));

        created.Error!.ValidationErrors.Keys.Order().ShouldBe(["filters", "hiddenColumns", "name", "sort"]);
    }

    [Fact]
    public void SetDefault_TogglesTheFlag()
    {
        var view = GridView.Create(Guid.CreateVersion7(), User, "cases.cases", Spec()).Value;

        view.SetDefault(true);
        view.IsDefault.ShouldBeTrue();
        view.SetDefault(false);
        view.IsDefault.ShouldBeFalse();
    }
}
