using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Configuration;
using Auxilia.Application.Platform.Modules;
using Auxilia.Application.Tests.Platform;
using Auxilia.Application.Tests.Platform.Modules;
using Auxilia.Contracts.Configuration;
using Auxilia.Diagnostics;
using Auxilia.Domain.Configuration;
using Auxilia.SharedKernel.Tenancy;

using NSubstitute;

namespace Auxilia.Application.Tests.Configuration;

/// <summary>F21 personal views: own views only, on grids the user's roles see; unique names, one default, at most 20.</summary>
public sealed class GridViewTests
{
    private const string Grid = "people.clients";

    private static readonly GridDefinition Clients = new(
        Grid, [new("name", "Name", Sortable: true, CanHide: false), new("email", "Email", Filterable: true)], [TenantRole.Administrator, TenantRole.Employee]);

    private InMemoryCustomizationData data { get; } = new();
    private readonly ICurrentUser user = Substitute.For<ICurrentUser>();
    private readonly ModuleRegistry modules = new([new TestModule("people", grids: [Clients])]);
    private readonly GridViewManager manager;
    private readonly GridViewQueryService query;
    private readonly Guid me = Guid.CreateVersion7();

    public GridViewTests()
    {
        user.ActorType.Returns(ActorType.User);
        user.UserId.Returns(me);
        user.Roles.Returns([TenantRole.Employee]);
        manager = new GridViewManager(ManagerHarness.Runner(), modules, user, data);
        query = new GridViewQueryService(modules, user, data);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SaveGridViewRequest Request(string name, bool isDefault = false) =>
        new(name, ["email"], new Dictionary<string, string> { ["email"] = "rossi" }, "-name", isDefault);

    [Fact]
    public async Task Create_ListUpdateDelete_OwnViewsByName()
    {
        var second = (await manager.CreateAsync(Grid, Request("Zeta"), Ct)).Value;
        var first = (await manager.CreateAsync(Grid, Request("Alpha"), Ct)).Value;

        (await query.ListMineAsync(Grid, Ct)).Value.Select(view => view.Name).ShouldBe(["Alpha", "Zeta"]);
        first.HiddenColumns.ShouldBe(["email"]);
        first.Filters.ShouldBe(new Dictionary<string, string> { ["email"] = "rossi" });
        first.Sort.ShouldBe("-name");

        var renamed = (await manager.UpdateAsync(Grid, second.Id, Request("Beta") with { Sort = null, Filters = null }, Ct)).Value;
        (renamed.Name, renamed.Sort, renamed.Filters.Count).ShouldBe(("Beta", null, 0));

        (await manager.DeleteAsync(Grid, first.Id, Ct)).IsSuccess.ShouldBeTrue();
        (await query.ListMineAsync(Grid, Ct)).Value.Select(view => view.Name).ShouldBe(["Beta"]);
    }

    [Fact]
    public async Task Default_IsOnlyOne()
    {
        var first = (await manager.CreateAsync(Grid, Request("First", isDefault: true), Ct)).Value;
        first.IsDefault.ShouldBeTrue();
        var second = (await manager.CreateAsync(Grid, Request("Second", isDefault: true), Ct)).Value;

        data.Views.Single(view => view.Id == first.Id).IsDefault.ShouldBeFalse();
        data.Views.Single(view => view.Id == second.Id).IsDefault.ShouldBeTrue();

        (await manager.UpdateAsync(Grid, second.Id, Request("Second"), Ct)).Value.IsDefault.ShouldBeFalse();
        data.Views.ShouldAllBe(view => !view.IsDefault);
    }

    [Fact]
    public async Task Names_AreUniqueWhateverTheCase_AndAtMostTwentyPerGrid()
    {
        await manager.CreateAsync(Grid, Request("Mine"), Ct);

        var taken = await manager.CreateAsync(Grid, Request("MINE"), Ct);
        taken.Error!.Code.ShouldBe(EventCodes.Configuration.GridViewInvalid);
        taken.Error.ValidationErrors["name"].ShouldBe(["validation.gridViews.nameTaken"]);

        for (var index = 1; index < GridView.MaxPerGrid; index++)
        {
            (await manager.CreateAsync(Grid, Request($"View {index}"), Ct)).IsSuccess.ShouldBeTrue();
        }

        (await manager.CreateAsync(Grid, Request("One more"), Ct)).Error!.ValidationErrors["name"].ShouldBe(["validation.gridViews.tooMany"]);
    }

    [Fact]
    public async Task OtherUsers_AndHiddenGrids_AreNotFound()
    {
        var mine = (await manager.CreateAsync(Grid, Request("Mine"), Ct)).Value;

        user.UserId.Returns(Guid.CreateVersion7());
        (await query.ListMineAsync(Grid, Ct)).Value.ShouldBeEmpty();
        (await manager.UpdateAsync(Grid, mine.Id, Request("Theirs"), Ct)).Error!.Code.ShouldBe(EventCodes.Configuration.GridViewNotFound);
        (await manager.DeleteAsync(Grid, mine.Id, Ct)).Error!.Code.ShouldBe(EventCodes.Configuration.GridViewNotFound);

        user.Roles.Returns([TenantRole.Client]);
        (await query.ListMineAsync(Grid, Ct)).Error!.Code.ShouldBe(EventCodes.Configuration.GridNotFound);
        (await manager.CreateAsync("people.unknown", Request("X"), Ct)).Error!.Code.ShouldBe(EventCodes.Configuration.GridNotFound);
        user.ActorType.Returns(ActorType.Platform);
        user.Roles.Returns([TenantRole.Administrator]);
        (await query.ListMineAsync(Grid, Ct)).Error!.Code.ShouldBe(EventCodes.Configuration.GridNotFound);
    }

    [Fact]
    public async Task InvalidViews_AreRefusedByTheDomain()
    {
        var created = await manager.CreateAsync(Grid, Request(" "), Ct);

        created.Error!.Code.ShouldBe(EventCodes.Configuration.GridViewInvalid);
        data.Views.ShouldBeEmpty();
    }
}
