using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Application.Abstractions.Tenancy;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Auxilia.Persistence.Tenant;

/// <inheritdoc cref="ITenantDbContextFactory"/>
internal sealed class TenantDbContextFactory : ITenantDbContextFactory
{
    private readonly ITenantContext tenantContext;
    private readonly ITenantDirectory directory;
    private readonly ITenantConnectionProtector protector;
    private readonly TenantDataSources dataSources;

    public TenantDbContextFactory(
        ITenantContext tenantContext, ITenantDirectory directory, ITenantConnectionProtector protector, TenantDataSources dataSources)
    {
        this.tenantContext = tenantContext;
        this.directory = directory;
        this.protector = protector;
        this.dataSources = dataSources;
    }

    public async Task<ITenantDbContext> CreateAsync(CancellationToken cancellationToken)
    {
        var tenant = tenantContext.Tenant;
        var protectedConnectionString = await directory.GetProtectedConnectionStringAsync(tenant.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenant.Slug} has no database yet.");

        var dataSource = dataSources.Get(tenant.Id, protector.Unprotect(protectedConnectionString));
        return new TenantDbContext(Options(dataSource));
    }

    internal static DbContextOptions<TenantDbContext> Options(NpgsqlDataSource dataSource) =>
        new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(dataSource, npgsql => npgsql
                .MigrationsAssembly(typeof(TenantDbContext).Assembly.GetName().Name)
                .MigrationsHistoryTable(TenantDbContext.MigrationsHistoryTable, TenantDbContext.MigrationsHistorySchema))
            .UseSnakeCaseNamingConvention()
            .Options;
}
