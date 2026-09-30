using Auxilia.Domain.Platform;
using Auxilia.Persistence.Catalog;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Auxilia.Persistence.Tests.Catalog;

[Collection(CatalogDatabaseGroup.Name)]
public sealed class CatalogMigrationTests(CatalogDatabaseFixture database)
{
    [Fact]
    public async Task Migrate_FromZero_CreatesEveryCatalogTable()
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "select table_name from information_schema.tables where table_schema = 'catalog' order by table_name", connection);

        var tables = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                tables.Add(reader.GetString(0));
            }
        }

        tables.ShouldBe(
        [
            "__ef_migrations_history", "client_applications", "data_protection_keys", "migration_runs", "modules",
            "plan_modules", "plans", "platform_refresh_tokens", "platform_sessions", "platform_settings", "platform_user_roles",
            "platform_user_tokens", "platform_users", "signing_keys", "tenant_domains",
            "tenant_module_overrides", "tenant_plans", "tenants",
        ]);
    }

    [Fact]
    public async Task Model_HasNoChangesMissingFromMigrations()
    {
        await using var db = database.CreateContext();

        db.Database.HasPendingModelChanges().ShouldBeFalse("run: dotnet ef migrations add Catalog_<Description>");
        (await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Migrate_SeedsTheDefaultStandardPlan()
    {
        await using var db = database.CreateContext();

        var plan = await db.Plans.SingleAsync(plan => plan.IsDefault, TestContext.Current.CancellationToken);

        plan.Id.ShouldBe(Plan.StandardId);
        plan.Code.ShouldBe(Plan.StandardCode);
        plan.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Migrate_TwiceIsANoOp()
    {
        await using var db = database.CreateContext();

        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);

        (await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).ShouldNotBeEmpty();
    }
}
