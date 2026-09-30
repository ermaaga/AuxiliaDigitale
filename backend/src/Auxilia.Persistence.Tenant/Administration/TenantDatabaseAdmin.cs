using System.Security.Cryptography;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Persistence.Tenant.DataMigrations;
using Auxilia.Persistence.Tenant.Interceptors;
using Auxilia.Persistence.Tenant.Identity;
using Auxilia.Persistence.Tenant.Seed;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Auxilia.Persistence.Tenant.Administration;

/// <summary>
/// Tenant databases on PostgreSQL (decision D-02): with a login that has <c>CREATEDB</c>/<c>CREATEROLE</c>, each tenant
/// gets a dedicated role <c>auxilia_t_&lt;slug&gt;</c> with a generated password, owning only its database
/// <c>auxilia_t_&lt;slug&gt;</c>; <c>CONNECT</c> is revoked from <c>PUBLIC</c>, so other tenants' roles cannot connect.
/// </summary>
internal sealed class TenantDatabaseAdmin : ITenantDatabaseAdmin
{
    private readonly TenantDatabaseAdminOptions options;
    private readonly DataMigrationRunner dataMigrations;
    private readonly TenantInitialSeed initialSeed;
    private readonly PermissionSynchronizer permissions;
    private readonly ICurrentUser currentUser;
    private readonly TimeProvider timeProvider;

    public TenantDatabaseAdmin(
        TenantDatabaseAdminOptions options,
        DataMigrationRunner dataMigrations,
        TenantInitialSeed initialSeed,
        PermissionSynchronizer permissions,
        ICurrentUser currentUser,
        TimeProvider timeProvider)
    {
        this.options = options;
        this.dataMigrations = dataMigrations;
        this.initialSeed = initialSeed;
        this.permissions = permissions;
        this.currentUser = currentUser;
        this.timeProvider = timeProvider;
    }

    public static string DatabaseName(string slug) => "auxilia_t_" + slug.Replace('-', '_');

    public async Task<string> CreateDatabaseAsync(string slug, CancellationToken cancellationToken)
    {
        if (!SharedKernel.Tenancy.TenantSlug.IsValid(slug))
        {
            throw new ArgumentException("Invalid tenant slug.", nameof(slug));
        }

        // Identifiers come from a validated slug and the password is base64url: safe to embed in DDL, which takes no parameters.
        var name = DatabaseName(slug);
        var password = Base64Url(RandomNumberGenerator.GetBytes(32));

        await using var dataSource = NpgsqlDataSource.Create(options.AdminConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var roleExists = await ExistsAsync(connection, "select 1 from pg_roles where rolname = @name", name, cancellationToken);
        await ExecuteAsync(connection, roleExists
            ? $"alter role \"{name}\" with login password '{password}'"
            : $"create role \"{name}\" with login password '{password}' nocreatedb nocreaterole", cancellationToken);

        if (!await ExistsAsync(connection, "select 1 from pg_database where datname = @name", name, cancellationToken))
        {
            await ExecuteAsync(connection, $"create database \"{name}\" owner \"{name}\"", cancellationToken);
        }

        await ExecuteAsync(connection, $"revoke connect on database \"{name}\" from public", cancellationToken);

        return new NpgsqlConnectionStringBuilder(options.AdminConnectionString)
        {
            Database = name,
            Username = name,
            Password = password,
        }.ConnectionString;
    }

    public async Task<bool> CanConnectAsync(string connectionString, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return true;
        }
        catch (NpgsqlException)
        {
            // Converted to AUX-11018 by the caller: the provided database cannot be used.
            return false;
        }
    }

    public async Task<TenantDatabaseVersion> MigrateAsync(string connectionString, bool isNewTenant, CancellationToken cancellationToken)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var db = new TenantDbContext(TenantDbContextOptions.Create(dataSource, new TenantAuditInterceptor(currentUser, timeProvider)));

        await db.Database.MigrateAsync(cancellationToken);
        if (isNewTenant)
        {
            await initialSeed.ApplyAsync(db, cancellationToken);
        }
        else
        {
            await dataMigrations.ApplyPendingAsync(db, cancellationToken);
        }

        // Permissions follow the deployed modules (new tenants get the default grants).
        await permissions.ApplyAsync(db, cancellationToken);

        var schemaVersion = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).LastOrDefault();
        return new TenantDatabaseVersion(schemaVersion, await DataMigrationRunner.CurrentVersionAsync(db, cancellationToken));
    }

    private static async Task<bool> ExistsAsync(NpgsqlConnection connection, string sql, string name, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("name", name);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Login used to create tenant databases and roles (<c>Provisioning:AdminConnectionString</c>, defaults to the Catalog login).</summary>
public sealed record TenantDatabaseAdminOptions(string AdminConnectionString);
