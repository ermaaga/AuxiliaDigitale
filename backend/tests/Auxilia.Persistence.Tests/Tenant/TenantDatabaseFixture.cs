using Auxilia.Persistence.Tenant;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Testcontainers.PostgreSql;

namespace Auxilia.Persistence.Tests.Tenant;

/// <summary>A PostgreSQL container with a tenant database migrated from zero.</summary>
public sealed class TenantDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18-alpine").WithDatabase("tenant_test").Build();

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public string ConnectionString => container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync();
        DataSource = NpgsqlDataSource.Create(ConnectionString);
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await container.DisposeAsync();
    }

    public TenantDbContext CreateContext() => new(TenantDbContextOptions.Create(DataSource));

    /// <summary>A separate empty database on the same server.</summary>
    public async Task<string> CreateDatabaseAsync(string name)
    {
        await using var connection = await DataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"create database {name}", connection);
        await command.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class TenantDatabaseGroup : ICollectionFixture<TenantDatabaseFixture>
{
    public const string Name = "Tenant database";
}
