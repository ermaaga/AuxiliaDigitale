using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Persistence.Tenant.DataMigrations;
using Auxilia.Persistence.Tenant.Seed;
using Auxilia.Persistence.Tenant.Transactions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Auxilia.Persistence.Tenant;

public static class TenantPersistence
{
    /// <summary>
    /// Tenant databases through <see cref="ITenantDbContextFactory"/> only (never a DbContext in DI), the operation
    /// transaction used by <see cref="IOperationRunner"/>, the EF Core exception classifier and the data-migrations.
    /// </summary>
    public static IServiceCollection AddTenantPersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<TenantDataSources>();
        services.AddScoped<TenantOperationTransactions>();
        services.Replace(ServiceDescriptor.Scoped<IOperationTransactionFactory>(provider => provider.GetRequiredService<TenantOperationTransactions>()));
        services.AddScoped<ITenantDbContextFactory, TenantDbContextFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionClassifier, EfCoreExceptionClassifier>());

        services.AddSingleton(_ => DataMigrationRunner.Discover(typeof(TenantDbContext).Assembly));
        services.AddSingleton(provider => new DataMigrationRunner(
            provider.GetRequiredService<IReadOnlyList<IDataMigration>>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System,
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DataMigrationRunner>>()));
        services.AddSingleton<TenantInitialSeed>();

        return services;
    }
}
