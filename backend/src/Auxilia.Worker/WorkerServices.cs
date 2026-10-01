using Auxilia.Application;
using Auxilia.Infrastructure;
using Auxilia.Infrastructure.Caching;
using Auxilia.Infrastructure.Messaging;
using Auxilia.Infrastructure.Realtime;
using Auxilia.Persistence.Catalog;
using Auxilia.Persistence.Tenant;
using Auxilia.Worker.Handlers.Platform;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Worker;

public static class WorkerServices
{
    /// <summary>
    /// Application, Catalog, tenant databases, cache and the message bus consuming every queue that has a handler in
    /// this assembly (<c>ConnectionStrings:Catalog</c> and <c>ConnectionStrings:RabbitMq</c> required).
    /// </summary>
    public static IServiceCollection AddWorkerServices(this IServiceCollection services, IConfiguration configuration, MessageBusOptions? busOptions = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddApplication();
        services.AddInfrastructure();

        // Same Catalog and tenant databases as the Api (user-secrets / environment, never committed).
        var catalog = configuration.GetConnectionString("Catalog")
            ?? throw new InvalidOperationException("ConnectionStrings:Catalog is not configured.");
        services.AddCatalogPersistence(catalog);
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

        var rabbitMq = configuration.GetConnectionString("RabbitMq")
            ?? throw new InvalidOperationException("ConnectionStrings:RabbitMq is not configured.");
        services.AddMessageBusWorker<RunRecurringJobHandler>(
            rabbitMq, catalog, MessageRouting.QueuesHandledBy(typeof(RunRecurringJobHandler).Assembly), busOptions);

        return services;
    }
}
