using Auxilia.Persistence.Tenant;
using Auxilia.Persistence.Tenant.DataMigrations;

namespace Auxilia.Architecture.Tests;

/// <summary>Data-migrations (skill auxilia-data-migration, parity F30): key format, uniqueness, class name mirrors the key.</summary>
public sealed class DataMigrationTests
{
    private static readonly IReadOnlyList<IDataMigration> Migrations = DataMigrationRunner.Discover(typeof(TenantDbContext).Assembly);

    [Fact]
    public void Keys_FollowTheFormatAndAreUnique()
    {
        Migrations.Where(migration => !DataMigrationRunner.IsValidKey(migration.Key)).Select(migration => migration.Key).ShouldBeEmpty();
        Migrations.Select(migration => migration.Key).ShouldBeUnique();
    }

    [Fact]
    public void ClassNames_StartWithTheKey()
    {
        Migrations
            .Where(migration => !migration.GetType().Name.StartsWith(migration.Key + "_", StringComparison.Ordinal))
            .Select(migration => migration.GetType().Name)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Descriptions_ArePresent()
    {
        Migrations.Where(migration => string.IsNullOrWhiteSpace(migration.Description)).Select(migration => migration.Key).ShouldBeEmpty();
    }
}
