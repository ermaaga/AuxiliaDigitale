using Auxilia.Domain.Configuration;
using Auxilia.Persistence.Tenant.Configuration;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tests.Tenant;

/// <summary>Custom field definitions (text[] options, keys unique per entity in any case) and grid layouts (jsonb columns).</summary>
[Collection(TenantDatabaseGroup.Name)]
public sealed class CustomizationPersistenceTests(TenantDatabaseFixture database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CustomFields_RoundTripAndKeysAreUniquePerEntityInAnyCase()
    {
        var entity = "e" + Guid.NewGuid().ToString("N")[..8];
        var spec = new CustomFieldSpec("Livello", CustomFieldType.Select, ["Alto", "Basso"], true, "Area", "#335cff", true, false, 2);
        await using (var data = new CustomizationData(database.CreateContext()))
        {
            data.Add(CustomFieldDefinition.Create(Guid.CreateVersion7(), entity, "Level", spec).Value);
            await data.SaveChangesAsync(Ct);
        }

        await using (var data = new CustomizationData(database.CreateContext()))
        {
            var stored = (await data.CustomFieldsAsync(entity, Ct)).ShouldHaveSingleItem();
            (stored.Key, stored.Type, stored.GroupName, stored.BadgeColor, stored.Order).ShouldBe(("Level", CustomFieldType.Select, "Area", "#335cff", 2));
            stored.Options.ShouldBe(["Alto", "Basso"]);
            stored.Update(spec with { Options = ["Alto", "Medio", "Basso"] }).IsSuccess.ShouldBeTrue();
            await data.SaveChangesAsync(Ct);
            (await data.FindCustomFieldAsync(stored.Id, Ct))!.Options.Count.ShouldBe(3);
        }

        await using var db = database.CreateContext();
        db.Set<CustomFieldDefinition>().Add(CustomFieldDefinition.Create(Guid.CreateVersion7(), entity, "LEVEL", spec).Value);
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task GridLayouts_KeepTheColumnsInOrderAndAreUniquePerGridAndRole()
    {
        var grid = "test.g" + Guid.NewGuid().ToString("N")[..8];
        await using (var data = new CustomizationData(database.CreateContext()))
        {
            data.Add(GridLayout.Create(Guid.CreateVersion7(), grid, "Employee", [new("b", true), new("a", false)]).Value);
            await data.SaveChangesAsync(Ct);
        }

        await using (var data = new CustomizationData(database.CreateContext()))
        {
            var layout = (await data.FindLayoutAsync(grid, "Employee", Ct))!;
            layout.Columns.ShouldBe([new("b", true), new("a", false)]);
            layout.Replace([new("a", true), new("b", true), new("c", false)]).IsSuccess.ShouldBeTrue();
            await data.SaveChangesAsync(Ct);
        }

        await using (var data = new CustomizationData(database.CreateContext()))
        {
            (await data.FindLayoutAsync(grid, "Employee", Ct))!.Columns.Select(column => column.Key).ShouldBe(["a", "b", "c"]);
            (await data.LayoutsAsync(Ct)).Count(layout => layout.GridKey == grid).ShouldBe(1);
            data.Remove((await data.FindLayoutAsync(grid, "Employee", Ct))!);
            await data.SaveChangesAsync(Ct);
            (await data.FindLayoutAsync(grid, "Employee", Ct)).ShouldBeNull();
        }

        await using var db = database.CreateContext();
        db.Set<GridLayout>().Add(GridLayout.Create(Guid.CreateVersion7(), grid, "Client", [new("a", true)]).Value);
        db.Set<GridLayout>().Add(GridLayout.Create(Guid.CreateVersion7(), grid, "Client", [new("a", true)]).Value);
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }
}
