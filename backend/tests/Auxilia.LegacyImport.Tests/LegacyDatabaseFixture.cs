using System.Reflection;

using Auxilia.Persistence.Tenant;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Testcontainers.PostgreSql;

namespace Auxilia.LegacyImport.Tests;

/// <summary>
/// One PostgreSQL server with the legacy schema at both baselines (D-30), created from the scripts generated from the
/// legacy EF migrations, a few seeded rows, and a migrated tenant database for the id map.
/// </summary>
public sealed class LegacyDatabaseFixture : IAsyncLifetime
{
    public const string Develop = "legacy_develop";
    public const string SecurityUpdate = "legacy_security_update";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18-alpine").WithDatabase("postgres_admin").Build();

    public string TenantConnectionString { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync();
        await CreateLegacyAsync(Develop, "legacy-develop.sql");
        await CreateLegacyAsync(SecurityUpdate, "legacy-security-update.sql");

        TenantConnectionString = await CreateDatabaseAsync("tenant");
        await using var tenant = NpgsqlDataSource.Create(TenantConnectionString);
        await using var db = new TenantDbContext(TenantDbContextOptions.Create(tenant));
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await container.DisposeAsync();

    public string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = database }.ConnectionString;

    /// <summary>A legacy database from a baseline script plus <paramref name="change"/> (e.g. a dropped table).</summary>
    public async Task<string> CreateLegacyAsync(string name, string script, string? change = null)
    {
        var connectionString = await CreateDatabaseAsync(name);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var sql in new[] { Script(script), Script("seed.sql"), change })
        {
            if (sql is null)
            {
                continue;
            }

            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        return connectionString;
    }

    public static string Script(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing fixture {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private async Task<string> CreateDatabaseAsync(string name)
    {
        await using var connection = new NpgsqlConnection(container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"create database {name}", connection);
        await command.ExecuteNonQueryAsync();
        return ConnectionString(name);
    }
}

[CollectionDefinition(Name)]
public sealed class LegacyDatabaseGroup : ICollectionFixture<LegacyDatabaseFixture>
{
    public const string Name = "Legacy database";
}
