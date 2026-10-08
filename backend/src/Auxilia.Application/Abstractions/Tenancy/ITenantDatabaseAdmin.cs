namespace Auxilia.Application.Abstractions.Tenancy;

/// <summary>
/// Creates and migrates tenant databases (decision D-02). Connection strings passed here are plain text: callers
/// store them only protected (<see cref="ITenantConnectionProtector"/>) and never log or print them.
/// </summary>
public interface ITenantDatabaseAdmin
{
    /// <summary>
    /// Creates (or re-creates the credentials of) the dedicated role and database of the tenant, idempotently, and
    /// returns the connection string of the new role.
    /// </summary>
    Task<string> CreateDatabaseAsync(string slug, CancellationToken cancellationToken);

    Task<bool> CanConnectAsync(string connectionString, CancellationToken cancellationToken);

    /// <summary>
    /// Schema migrations, then the initial seed (<paramref name="isNewTenant"/>) or the pending data-migrations.
    /// </summary>
    Task<TenantDatabaseVersion> MigrateAsync(string connectionString, bool isNewTenant, CancellationToken cancellationToken);
}

public sealed record TenantDatabaseVersion(string? SchemaVersion, string? DataVersion);

/// <summary>The newest schema migration of the tenant databases this build carries (F32: readiness reports tenants behind it).</summary>
public interface ITenantSchemaInfo
{
    string? LatestSchemaVersion { get; }
}
