using Auxilia.Application;
using Auxilia.Infrastructure;
using Auxilia.Infrastructure.Adapters.Storage.Local;
using Auxilia.Infrastructure.Caching;
using Auxilia.Infrastructure.Logging;
using Auxilia.Infrastructure.Messaging;
using Auxilia.Infrastructure.Realtime;
using Auxilia.Persistence.Catalog;
using Auxilia.Persistence.Tenant;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Worker;

public static class WorkerServices
{
    /// <summary>
    /// Application, Catalog, tenant databases, cache and the message bus consuming every queue that has a handler
    /// (<see cref="MessageRouting.HandledQueues"/>; <c>ConnectionStrings:Catalog</c> and <c>ConnectionStrings:RabbitMq</c>
    /// required). With <c>Messaging:Mode = InProcess</c> the Api consumes the queues and the Worker must not run. In Development
    /// (<paramref name="isDevelopment"/>) the Data Protection keys are never protected with Key Vault.
    /// </summary>
    public static IServiceCollection AddWorkerServices(this IServiceCollection services, IConfiguration configuration, MessageBusOptions? busOptions = null, bool isDevelopment = false)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddApplication();
        services.AddInfrastructure();
        services.Configure<LocalStorageOptions>(configuration.GetSection(LocalStorageOptions.SectionName));
        services.Configure<Infrastructure.Adapters.OAuth.ConnectedAccountsOptions>(configuration.GetSection(Infrastructure.Adapters.OAuth.ConnectedAccountsOptions.SectionName));
        services.AddTenantLogLevelSync();

        // Same Catalog and tenant databases as the Api (user-secrets / environment, never committed).
        var catalog = configuration.GetConnectionString("Catalog")
            ?? throw new InvalidOperationException("ConnectionStrings:Catalog is not configured.");
        services.AddCatalogPersistence(catalog, DataProtectionKeyVault.From(
            configuration[DataProtectionKeyVault.KeyIdSetting], configuration[DataProtectionKeyVault.ManagedIdentityClientIdSetting], isDevelopment));
        services.AddTenantPersistence();

        // Provisioning requested from the console (ProvisionTenantCommand): creates tenant databases.
        services.AddTenantAdministration();
        services.AddTenantDatabaseAdministration(configuration["Provisioning:AdminConnectionString"] ?? catalog);

        if (configuration.GetConnectionString("Redis") is { Length: > 0 } redis)
        {
            services.AddRedisCache(redis);
        }

        // Pushes to the Api's hub connections go through the Redis backplane (without Redis they are dropped).
        services.AddRealtime(configuration.GetConnectionString("Redis"));

        if (MessagingModeSetting.Parse(configuration[MessagingModeSetting.Key]) == MessagingMode.InProcess)
        {
            throw new InvalidOperationException($"{MessagingModeSetting.Key} is {MessagingMode.InProcess}: the Api consumes the queues, do not run the Worker.");
        }

        var rabbitMq = configuration.GetConnectionString("RabbitMq")
            ?? throw new InvalidOperationException("ConnectionStrings:RabbitMq is not configured.");
        services.AddMessageBusWorker(rabbitMq, catalog, busOptions);

        return services;
    }
}
