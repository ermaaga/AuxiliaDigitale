using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tests.Tenant;

[Collection(TenantDatabaseGroup.Name)]
public sealed class TenantMigrationTests(TenantDatabaseFixture database)
{
    [Fact]
    public async Task Migrate_FromZero_CreatesOpsAndAuditTables()
    {
        await using var db = database.CreateContext();

        var tables = await db.Database.SqlQueryRaw<string>(
            "select table_schema || '.' || table_name as \"Value\" from information_schema.tables where table_schema in ('ops', 'audit') order by 1")
            .ToListAsync(TestContext.Current.CancellationToken);

        tables.ShouldBe(
        [
            "audit.entity_changes", "ops.__ef_migrations_history", "ops.data_migrations_history", "ops.job_runs",
            "ops.legacy_id_map", "ops.number_sequences", "ops.outbox_messages", "ops.processed_messages",
        ]);
    }

    [Fact]
    public async Task Model_HasNoChangesMissingFromMigrations()
    {
        await using var db = database.CreateContext();

        db.Database.HasPendingModelChanges().ShouldBeFalse("run: dotnet ef migrations add <Module>_<Description> for TenantDbContext");
        (await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }
}
