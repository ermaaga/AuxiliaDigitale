using System.Text.Json;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Configuration;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Configuration;
using Auxilia.Application.Platform.Modules;
using Auxilia.Application.Tests.Platform;
using Auxilia.Application.Tests.Platform.Modules;
using Auxilia.Contracts.Configuration;
using Auxilia.Diagnostics;
using Auxilia.Domain.Configuration;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using NSubstitute;

namespace Auxilia.Application.Tests.Configuration;

/// <summary>In-memory custom field definitions and grid layouts shared by every unit of work of a test.</summary>
internal sealed class InMemoryCustomizationData : ICustomizationDataFactory, ICustomizationData
{
    public List<CustomFieldDefinition> Fields { get; } = [];

    public List<GridLayout> Layouts { get; } = [];

    public Task<ICustomizationData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<ICustomizationData>(this);

    public Task<IReadOnlyList<CustomFieldDefinition>> CustomFieldsAsync(string? entityType, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CustomFieldDefinition>>(Fields
            .Where(field => entityType is null || field.EntityType == entityType)
            .OrderBy(field => field.EntityType).ThenBy(field => field.Order).ThenBy(field => field.Label).ToList());

    public Task<CustomFieldDefinition?> FindCustomFieldAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Fields.SingleOrDefault(field => field.Id == id));

    public void Add(CustomFieldDefinition definition) => Fields.Add(definition);

    public void Remove(CustomFieldDefinition definition) => Fields.Remove(definition);

    public Task<IReadOnlyList<GridLayout>> LayoutsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GridLayout>>(Layouts.ToList());

    public Task<GridLayout?> FindLayoutAsync(string gridKey, string role, CancellationToken cancellationToken) =>
        Task.FromResult(Layouts.SingleOrDefault(layout => layout.GridKey == gridKey && layout.Role == role));

    public void Add(GridLayout layout) => Layouts.Add(layout);

    public void Remove(GridLayout layout) => Layouts.Remove(layout);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class CustomizationTests
{
    private static readonly TenantInfo Acme = new(Guid.CreateVersion7(), "acme", TenantStatus.Active, "it", "Europe/Rome");

    private static readonly GridDefinition Clients = new(
        "people.clients",
        [new("name", "Name", Sortable: true, CanHide: false), new("email", "Email", Filterable: true), new("notes", "Notes", VisibleByDefault: false)],
        [TenantRole.Administrator, TenantRole.Employee]);

    private InMemoryCustomizationData data { get; } = new();
    private readonly FakeReferenceDataCache cache = new();
    private readonly ITenantContext tenantContext = Substitute.For<ITenantContext>();
    private readonly ICurrentUser user = Substitute.For<ICurrentUser>();
    private readonly ModuleRegistry modules = new([new TestModule("people", grids: [Clients], customFieldEntities: [new("client")])]);
    private readonly CustomFieldManager fields;
    private readonly CustomFieldQueryService fieldQuery;
    private readonly CustomFieldValidator validator;
    private readonly GridLayoutManager layouts;
    private readonly GridQueryService gridQuery;

    public CustomizationTests()
    {
        tenantContext.Current.Returns(Acme);
        tenantContext.Tenant.Returns(Acme);
        tenantContext.IsResolved.Returns(true);
        user.ActorType.Returns(ActorType.User);
        user.Roles.Returns([TenantRole.Employee, TenantRole.Client]);

        var fieldCache = new CustomFieldCache(cache, tenantContext, data);
        fields = new CustomFieldManager(ManagerHarness.Runner(), data, modules, tenantContext, cache);
        fieldQuery = new CustomFieldQueryService(modules, fieldCache);
        validator = new CustomFieldValidator(modules, fieldCache);
        layouts = new GridLayoutManager(ManagerHarness.Runner(), data, modules, tenantContext, cache);
        gridQuery = new GridQueryService(modules, new GridLayoutCache(cache, tenantContext, data), user);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CreateCustomFieldRequest Field(string key, string type = "Boolean", IReadOnlyList<string>? options = null, bool required = false, int order = 0) =>
        new("client", key, key.ToUpperInvariant(), type, options, required, null, null, true, false, order);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Registry_ValidatesGridsAndEntities()
    {
        Should.Throw<InvalidOperationException>(() => new ModuleRegistry([new TestModule("people", grids: [Clients with { Key = "other.clients" }])]));
        Should.Throw<InvalidOperationException>(() => new ModuleRegistry([new TestModule("people", grids: [Clients with { Columns = [new("name", "Name")] }])]));
        Should.Throw<InvalidOperationException>(() => new ModuleRegistry([new TestModule("people", grids: [Clients with { Roles = [] }])]));
        Should.Throw<InvalidOperationException>(() => new ModuleRegistry([new TestModule("people", grids: [Clients, Clients])]));
        Should.Throw<InvalidOperationException>(() => new ModuleRegistry([new TestModule("people", customFieldEntities: [new("Client")])]));
        Should.Throw<InvalidOperationException>(() => new ModuleRegistry(
            [new TestModule("people", customFieldEntities: [new("client")]), new TestModule("other", rangeStart: 91000, customFieldEntities: [new("client")])]));

        modules.Grids.Keys.ShouldBe(["people.clients"]);
        modules.CustomFieldEntities["client"].ShouldBe("people");
        fieldQuery.ListEntities().ShouldHaveSingleItem().NameKey.ShouldBe("customFields.entities.client");
    }

    [Fact]
    public async Task CustomFields_AreCreatedUpdatedDeletedAndCachedPerTenant()
    {
        var id = (await fields.CreateAsync(Field("CAF", order: 2), Ct)).Value;
        (await fields.CreateAsync(Field("note", "Text", order: 1), Ct)).IsSuccess.ShouldBeTrue();
        cache.Invalidated.ShouldBe(["t:acme:configuration", "t:acme:configuration"]);

        (await fieldQuery.ListAsync("client", Ct)).Value.Select(field => field.Key).ShouldBe(["note", "CAF"]);
        (await fields.CreateAsync(Field("caf"), Ct)).Error!.Code.ShouldBe(EventCodes.Configuration.CustomFieldKeyTaken);
        (await fields.CreateAsync(Field("x") with { EntityType = "unknown" }, Ct)).Error!.ValidationErrors!.Keys.ShouldBe(["entityType"]);
        (await fields.CreateAsync(Field("x", "Colour"), Ct)).Error!.ValidationErrors!.Keys.ShouldBe(["type"]);
        (await fieldQuery.ListAsync("unknown", Ct)).Error!.Code.ShouldBe(EventCodes.Configuration.CustomFieldInvalid);

        (await fields.UpdateAsync(id, new UpdateCustomFieldRequest("Caf", null, false, "Area", "#72fa29", true, true, 0), Ct)).IsSuccess.ShouldBeTrue();
        var updated = (await fieldQuery.ListAsync(null, Ct)).Value.Single(field => field.Id == id);
        (updated.Label, updated.GroupName, updated.BadgeColor, updated.DashboardCounter, updated.Type).ShouldBe(("Caf", "Area", "#72fa29", true, "Boolean"));

        (await fields.UpdateAsync(Guid.CreateVersion7(), new UpdateCustomFieldRequest("x", null, false, null, null, true, false, 0), Ct)).Error!.Code
            .ShouldBe(EventCodes.Configuration.CustomFieldNotFound);
        (await fields.DeleteAsync(id, Ct)).IsSuccess.ShouldBeTrue();
        (await fields.DeleteAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Configuration.CustomFieldNotFound);
        (await fieldQuery.ListAsync("client", Ct)).Value.ShouldHaveSingleItem().Key.ShouldBe("note");
    }

    [Fact]
    public async Task Validator_NormalisesValuesAndReportsEveryField()
    {
        await fields.CreateAsync(Field("CAF"), Ct);
        await fields.CreateAsync(Field("notes", "Text", required: true), Ct);
        await fields.CreateAsync(Field("since", "Date"), Ct);
        await fields.CreateAsync(Field("amount", "Number"), Ct);
        await fields.CreateAsync(Field("level", "Select", ["low", "high"]), Ct);
        await fields.CreateAsync(Field("areas", "MultiSelect", ["a", "b", "c"]), Ct);

        var valid = await validator.ValidateAsync("client",
            Json("""{"CAF":true,"notes":"  hello ","since":"2026-01-31","amount":12.50,"level":"high","areas":["c","a"]}"""), Ct);
        JsonDocument.Parse(valid.Value).RootElement.GetRawText().ShouldBe(
            """{"amount":12.50,"areas":["a","c"],"CAF":true,"level":"high","notes":"hello","since":"2026-01-31"}""");

        var invalid = await validator.ValidateAsync("client",
            Json("""{"CAF":"yes","notes":"  ","since":"31/01/2026","amount":"12","level":"medium","areas":["a","a"],"ghost":1}"""), Ct);
        invalid.Error!.Code.ShouldBe(EventCodes.Configuration.CustomFieldValuesInvalid);
        invalid.Error.ValidationErrors!.Select(error => (error.Key, error.Value.Single())).ShouldBe(
        [
            ("customFields.ghost", "validation.customFields.unknown"),
            ("customFields.CAF", "validation.customFields.value"),
            ("customFields.notes", "validation.customFields.required"),
            ("customFields.since", "validation.customFields.value"),
            ("customFields.amount", "validation.customFields.value"),
            ("customFields.level", "validation.customFields.value"),
            ("customFields.areas", "validation.customFields.value"),
        ], ignoreOrder: true);

        (await validator.ValidateAsync("client", null, Ct)).Error!.ValidationErrors!.Keys.ShouldBe(["customFields.notes"]);
        (await validator.ValidateAsync("client", Json("[1]"), Ct)).Error!.ValidationErrors!.Keys.ShouldBe(["customFields"]);
        (await validator.ValidateAsync("client", Json("""{"notes":"x","CAF":null,"areas":[]}"""), Ct)).Value.ShouldBe("""{"notes":"x"}""");
        await Should.ThrowAsync<ArgumentException>(() => validator.ValidateAsync("ghost", null, Ct));
    }

    [Fact]
    public async Task GridLayouts_DefaultSetAndResetPerRole()
    {
        var grid = (await gridQuery.ListAsync(Ct)).ShouldHaveSingleItem();
        grid.Module.ShouldBe("people");
        grid.Layouts.Select(layout => (layout.Role, layout.IsCustomized)).ShouldBe([("Administrator", false), ("Employee", false)]);
        grid.Layouts[0].Columns.ShouldBe([new("name", true), new("email", true), new("notes", false)]);

        var set = (await layouts.SetAsync("people.clients", "Employee", [new("notes", true), new("name", true)], Ct)).Value;
        set.IsCustomized.ShouldBeTrue();
        set.Columns.ShouldBe([new("notes", true), new("name", true), new("email", true)]);
        cache.Invalidated.ShouldBe(["t:acme:configuration"]);

        var mine = (await gridQuery.GetMineAsync("people.clients", Ct)).Value;
        (mine.Role, mine.IsCustomized).ShouldBe(("Employee", true));
        mine.Columns.Select(column => column.Key).ShouldBe(["notes", "name", "email"]);

        (await layouts.SetAsync("people.clients", "Employee", [new("email", false)], Ct)).Value.Columns
            .ShouldBe([new("email", false), new("name", true), new("notes", false)]);
        data.Layouts.ShouldHaveSingleItem();

        (await layouts.SetAsync("people.clients", "Employee", [new("name", false)], Ct)).Error!.ValidationErrors!["columns"].ShouldBe(["validation.grids.mustStayVisible"]);
        (await layouts.SetAsync("people.clients", "Employee", [new("ghost", true)], Ct)).Error!.ValidationErrors!["columns"].ShouldBe(["validation.grids.unknownColumn"]);
        (await layouts.SetAsync("people.clients", "Client", [new("name", true)], Ct)).Error!.Code.ShouldBe(EventCodes.Configuration.GridNotFound);
        (await layouts.SetAsync("people.unknown", "Employee", [], Ct)).Error!.Code.ShouldBe(EventCodes.Configuration.GridNotFound);

        var reset = (await layouts.ResetAsync("people.clients", "Employee", Ct)).Value;
        reset.IsCustomized.ShouldBeFalse();
        data.Layouts.ShouldBeEmpty();
        (await layouts.ResetAsync("people.clients", "Employee", Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task MyLayout_FollowsTheCatalogAndTheUsersFirstRole()
    {
        // A stored layout written for an older catalog: unknown columns go, new ones are appended, fixed ones shown.
        data.Layouts.Add(GridLayout.Create(Guid.CreateVersion7(), "people.clients", "Employee", [new("removed", true), new("name", false)]).Value);

        var mine = (await gridQuery.GetMineAsync("people.clients", Ct)).Value;
        mine.Columns.ShouldBe([new("name", true), new("email", true), new("notes", false)]);

        user.Roles.Returns([TenantRole.Client]);
        (await gridQuery.GetMineAsync("people.clients", Ct)).Error!.Code.ShouldBe(EventCodes.Configuration.GridNotFound);
        user.ActorType.Returns(ActorType.Platform);
        user.Roles.Returns([TenantRole.Administrator]);
        (await gridQuery.GetMineAsync("people.clients", Ct)).Error!.Code.ShouldBe(EventCodes.Configuration.GridNotFound);
        (await gridQuery.GetMineAsync("people.unknown", Ct)).IsFailure.ShouldBeTrue();
    }
}
