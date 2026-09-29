using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Persistence.Tenant.Interceptors;
using Auxilia.Persistence.Tenant.Transactions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Npgsql;

namespace Auxilia.Persistence.Tenant;

/// <inheritdoc cref="ITenantDbContextFactory"/>
internal sealed class TenantDbContextFactory : ITenantDbContextFactory
{
    private readonly ITenantContext tenantContext;
    private readonly ITenantDirectory directory;
    private readonly ITenantConnectionProtector protector;
    private readonly TenantDataSources dataSources;
    private readonly TenantOperationTransactions transactions;
    private readonly ICurrentUser currentUser;
    private readonly TimeProvider timeProvider;

    public TenantDbContextFactory(
        ITenantContext tenantContext,
        ITenantDirectory directory,
        ITenantConnectionProtector protector,
        TenantDataSources dataSources,
        TenantOperationTransactions transactions,
        ICurrentUser currentUser,
        TimeProvider timeProvider)
    {
        this.tenantContext = tenantContext;
        this.directory = directory;
        this.protector = protector;
        this.dataSources = dataSources;
        this.transactions = transactions;
        this.currentUser = currentUser;
        this.timeProvider = timeProvider;
    }

    public async Task<ITenantDbContext> CreateAsync(CancellationToken cancellationToken)
    {
        var tenant = tenantContext.Tenant;
        var protectedConnectionString = await directory.GetProtectedConnectionStringAsync(tenant.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenant.Slug} has no database yet.");
        var dataSource = dataSources.Get(tenant.Id, protector.Unprotect(protectedConnectionString));
        var audit = new TenantAuditInterceptor(currentUser, timeProvider);

        if (!transactions.IsActive)
        {
            return new TenantDbContext(TenantDbContextOptions.Create(dataSource, audit));
        }

        // Inside a write operation: share the operation's connection and transaction (committed by the runner).
        var (connection, transaction) = await transactions.EnlistAsync(dataSource, cancellationToken);
        var context = new TenantDbContext(TenantDbContextOptions.Create(connection, audit));
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        return context;
    }
}

/// <summary>Provider options of every <see cref="TenantDbContext"/> (runtime, auxctl, design time, tests).</summary>
public static class TenantDbContextOptions
{
    public static DbContextOptions<TenantDbContext> Create(NpgsqlDataSource dataSource, params IInterceptor[] interceptors) =>
        Build(builder => builder.UseNpgsql(dataSource, Npgsql), interceptors);

    public static DbContextOptions<TenantDbContext> Create(System.Data.Common.DbConnection connection, params IInterceptor[] interceptors) =>
        Build(builder => builder.UseNpgsql(connection, Npgsql), interceptors);

    private static DbContextOptions<TenantDbContext> Build(
        Action<DbContextOptionsBuilder<TenantDbContext>> provider, IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<TenantDbContext>();
        provider(builder);
        return builder.UseSnakeCaseNamingConvention().AddInterceptors(interceptors).Options;
    }

    private static void Npgsql(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql
            .MigrationsAssembly(typeof(TenantDbContext).Assembly.GetName().Name)
            .MigrationsHistoryTable(TenantDbContext.MigrationsHistoryTable, TenantSchemas.Ops);
}
