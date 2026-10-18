using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Channels;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Persistence.Catalog.Configuration;
using Auxilia.Persistence.Catalog.Identity;
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
    /// Needs <c>IReferenceDataCache</c> (<c>AddInfrastructure</c>) for the tenant lookups. With <paramref name="keyVault"/>
    /// the keys are protected at rest by a Key Vault key (#141, R4); without it (Development) they are stored as they are.
    /// </summary>
    public static IServiceCollection AddCatalogPersistence(this IServiceCollection services, string connectionString, DataProtectionKeyVault? keyVault = null)
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

        var dataProtection = services.AddDataProtection()
            .SetApplicationName(DataProtectionApplicationName)
            .PersistKeysToDbContext<CatalogDbContext>();
        if (keyVault is not null)
        {
            // The key wraps and unwraps each key of the ring inside the vault: a copy of the Catalog alone decrypts nothing.
            dataProtection.ProtectKeysWithAzureKeyVault(keyVault.KeyId, keyVault.Credential());
        }

        services.AddScoped<ITenantDirectory, CatalogTenantDirectory>();
        services.AddScoped<ICatalogStore, CatalogStore>();
        services.AddScoped<ICatalogMigrator, CatalogMigrator>();
        services.AddSingleton<ITenantConnectionProtector, TenantConnectionProtector>();
        services.AddScoped<IPlatformSettingStore, PlatformSettingStore>();
        services.AddScoped<IModuleCatalogReader, ModuleCatalogReader>();
        services.AddScoped<ISigningKeyStore, SigningKeyStore>();
        services.AddScoped<IClientApplicationStore, ClientApplicationStore>();
        services.AddSingleton<ISettingSecretProtector, SettingSecretProtector>();
        services.AddSingleton<IAccountSecretProtector, AccountSecretProtector>();
        services.AddSingleton<ISigningKeyProtector, SigningKeyProtector>();
        services.AddSingleton<ITwoFactorSecretProtector, TwoFactorSecretProtector>();
        services.AddScoped<IUserTwoFactorSecretProtector, UserTwoFactorSecretProtector>();
        services.AddScoped<Application.Abstractions.Mailbox.IConnectedAccountSecretProtector, ConnectedAccountSecretProtector>();
        services.AddScoped<IPlatformIdentityStore, PlatformIdentityStore>();

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
