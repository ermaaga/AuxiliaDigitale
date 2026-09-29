using Auxilia.Domain.Platform;

using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Catalog;

/// <summary>
/// The Catalog DB (schema <c>catalog</c>, ADR 0002): tenants, plans and modules, platform users and settings,
/// client applications, migration runs and the Data Protection key ring. The only database whose connection
/// string is in configuration (<c>ConnectionStrings:Catalog</c>).
/// </summary>
public sealed class CatalogDbContext : DbContext, IDataProtectionKeyContext
{
    public const string Schema = "catalog";

    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public CatalogDbContext(DbContextOptions<CatalogDbContext> options)
        : base(options)
    {
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<TenantDomain> TenantDomains => Set<TenantDomain>();

    public DbSet<PlatformModule> Modules => Set<PlatformModule>();

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<TenantPlan> TenantPlans => Set<TenantPlan>();

    public DbSet<TenantModuleOverride> TenantModuleOverrides => Set<TenantModuleOverride>();

    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();

    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();

    public DbSet<ClientApplication> ClientApplications => Set<ClientApplication>();

    public DbSet<MigrationRun> MigrationRuns => Set<MigrationRun>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
    }
}
