using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Persistence.Tenant.Conventions;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant;

/// <summary>
/// The database of one tenant (database-per-tenant, ADR 0002): one PostgreSQL schema per module
/// (<see cref="TenantSchemas"/>). Created only by <see cref="TenantDbContextFactory"/>, never registered in DI.
/// Conventions: audit columns, <c>xmin</c>, soft delete (<see cref="TenantConventions"/>).
/// </summary>
public sealed class TenantDbContext : DbContext, ITenantDbContext
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public TenantDbContext(DbContextOptions<TenantDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TenantDbContext).Assembly);
        TenantConventions.Apply(modelBuilder);
    }
}
