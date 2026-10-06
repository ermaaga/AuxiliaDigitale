using Auxilia.MigrationRunner.LegacyImport;
using Auxilia.Persistence.Tenant;
using Auxilia.Persistence.Tenant.Operations;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Auxilia.LegacyImport.Tests;

/// <summary><c>ops.legacy_id_map</c> (task E-01): a legacy row keeps its Guid across runs.</summary>
[Collection(LegacyDatabaseGroup.Name)]
public sealed class LegacyIdMapTests(LegacyDatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetOrAdd_SecondRun_ReturnsTheSameGuid()
    {
        await using var source = NpgsqlDataSource.Create(fixture.TenantConnectionString);
        Guid first;
        await using (var db = new TenantDbContext(TenantDbContextOptions.Create(source)))
        {
            var map = await LegacyIdMap.LoadAsync(db, TimeProvider.System, Ct);
            var (id, created) = map.GetOrAdd("Subscriptions", 42);
            created.ShouldBeTrue();
            map.GetOrAdd("Subscriptions", 42).ShouldBe((id, false));
            map.Find("Subscriptions", 43).ShouldBeNull();
            await db.SaveChangesAsync(Ct);
            first = id;
        }

        await using (var db = new TenantDbContext(TenantDbContextOptions.Create(source)))
        {
            var map = await LegacyIdMap.LoadAsync(db, TimeProvider.System, Ct);
            map.Find("Subscriptions", 42).ShouldBe(first);
            map.GetOrAdd("Subscriptions", 42).ShouldBe((first, false));
            map.Counts()["Subscriptions"].ShouldBe(1);
            (await LegacyIdMap.CountsAsync(db, Ct))["Subscriptions"].ShouldBe(1);
            (await db.Set<LegacyIdMapping>().SingleAsync(row => row.Entity == "Subscriptions" && row.LegacyId == 42, Ct)).NewId.ShouldBe(first);
        }
    }

    [Theory]
    [InlineData("Subscription")]
    [InlineData("WorkoutPlans")]
    public async Task GetOrAdd_TableNotMigrated_IsRefused(string entity)
    {
        await using var source = NpgsqlDataSource.Create(fixture.TenantConnectionString);
        await using var db = new TenantDbContext(TenantDbContextOptions.Create(source));
        var map = await LegacyIdMap.LoadAsync(db, TimeProvider.System, Ct);

        Should.Throw<ArgumentException>(() => map.GetOrAdd(entity, 1));
    }
}
