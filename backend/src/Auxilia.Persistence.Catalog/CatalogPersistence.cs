using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Persistence.Catalog.Configuration;
using Auxilia.Persistence.Catalog.Interceptors;
using Auxilia.Persistence.Catalog.Modules;
using Auxilia.Persistence.Catalog.Tenancy;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Auxilia.Persistence.Catalog;

public static class CatalogPersistence
{
    /// <summary>Data Protection application name: keys are shared by Api, Worker and auxctl.</summary>
    public const string DataProtectionApplicationName = "Auxilia";

    /// <summary>
    /// <see cref="CatalogDbContext"/> on PostgreSQL (snake_case names, schema <c>catalog</c>) and the Data Protection
    /// key ring persisted in <c>catalog.data_protection_keys</c>, platform settings and the protector of secret settings.
    /// Needs <c>IReferenceDataCache</c> (<c>AddInfrastructure</c>) for the tenant lookups. Protecting the keys at rest with a certificate is
    /// configured by the host from its secret store (H-03).
    /// </summary>
    public static IServiceCollection AddCatalogPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<CatalogDbContext>((provider, options) =>
        {
            Configure(options, connectionString);
            options.AddInterceptors(new CatalogAuditInterceptor(
                provider.GetRequiredService<ICurrentUser>(),
                provider.GetService<TimeProvider>() ?? TimeProvider.System));
        });

        services.AddDataProtection()
            .SetApplicationName(DataProtectionApplicationName)
            .PersistKeysToDbContext<CatalogDbContext>();

        services.AddScoped<ITenantDirectory, CatalogTenantDirectory>();
        services.AddScoped<ICatalogStore, CatalogStore>();
        services.AddScoped<ICatalogMigrator, CatalogMigrator>();
        services.AddSingleton<ITenantConnectionProtector, TenantConnectionProtector>();
        services.AddScoped<IPlatformSettingStore, PlatformSettingStore>();
        services.AddScoped<IModuleCatalogReader, ModuleCatalogReader>();
        services.AddSingleton<ISettingSecretProtector, SettingSecretProtector>();

        return services;
    }

    /// <summary>Provider options shared by the runtime registration and the design-time factory.</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options
            .UseNpgsql(connectionString, npgsql => ConfigureNpgsql(npgsql))
            .UseSnakeCaseNamingConvention();
    }

    private static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql
            .MigrationsAssembly(typeof(CatalogDbContext).Assembly.GetName().Name)
            .MigrationsHistoryTable(CatalogDbContext.MigrationsHistoryTable, CatalogDbContext.Schema);
}
