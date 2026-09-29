using Auxilia.Application.Abstractions.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant;

/// <summary>
/// The database of one tenant (database-per-tenant, ADR 0002): one PostgreSQL schema per module. Created only by
/// <see cref="TenantDbContextFactory"/>. Schemas, audit interceptor, soft delete and migrations arrive with P1-08.
/// </summary>
public sealed class TenantDbContext : DbContext, ITenantDbContext
{
    public const string MigrationsHistorySchema = "ops";

    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public TenantDbContext(DbContextOptions<TenantDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TenantDbContext).Assembly);
    }
}
