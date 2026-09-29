using Auxilia.Persistence.Catalog;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Auxilia.MigrationRunner.DesignTime;

/// <summary>
/// Used by <c>dotnet ef</c> (startup project = MigrationRunner). Adding a migration needs no database; commands that
/// connect read <c>ConnectionStrings__Catalog</c> from the environment (never a committed password).
/// </summary>
internal sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Catalog")
            ?? "Host=localhost;Database=auxilia_catalog";

        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        CatalogPersistence.Configure(options, connectionString);

        return new CatalogDbContext(options.Options);
    }
}
