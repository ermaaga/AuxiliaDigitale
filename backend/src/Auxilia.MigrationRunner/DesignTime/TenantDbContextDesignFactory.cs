using Auxilia.Persistence.Tenant;

using Microsoft.EntityFrameworkCore.Design;

using Npgsql;

namespace Auxilia.MigrationRunner.DesignTime;

/// <summary>
/// Used by <c>dotnet ef</c> for the tenant migrations. Adding a migration needs no database; commands that connect
/// read <c>AUXILIA_DESIGN_TENANT_DB</c> from the environment (never a committed password).
/// </summary>
internal sealed class TenantDbContextDesignFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("AUXILIA_DESIGN_TENANT_DB") ?? "Host=localhost;Database=auxilia_tenant_design";
        var dataSource = NpgsqlDataSource.Create(connectionString);
        return new TenantDbContext(TenantDbContextOptions.Create(dataSource));
    }
}
