using Auxilia.Application;
using Auxilia.Infrastructure;
using Auxilia.Infrastructure.Adapters.Storage.Local;
using Auxilia.Infrastructure.Caching;
using Auxilia.Infrastructure.Messaging;
using Auxilia.Persistence.Catalog;
using Auxilia.Persistence.Tenant;
using Auxilia.ServiceDefaults.Logging;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Auxilia.MigrationRunner;

/// <summary>
/// Services of auxctl: logging to the same per-tenant daily files as Api and Worker (F25), the application base,
/// the Catalog (<c>ConnectionStrings:Catalog</c>) and tenant database administration
/// (<c>Provisioning:AdminConnectionString</c>, default the Catalog login, which needs CREATEDB/CREATEROLE — D-02) and, when
/// <c>ConnectionStrings:Redis</c> is set, the Redis cache (to invalidate the tenant lookups of the other nodes).
/// </summary>
internal static class AuxctlHost
{
    public static IHost Build(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ApplicationName = "Auxilia.MigrationRunner" });
        if (overrides is not null)
        {
            builder.Configuration.AddInMemoryCollection(overrides);
        }

        var catalog = builder.Configuration.GetConnectionString("Catalog")
            ?? throw new InvalidOperationException("ConnectionStrings:Catalog is not configured (environment ConnectionStrings__Catalog or user-secrets).");

        builder.AddAuxiliaLogging();
        builder.Services.AddApplication();
        builder.Services.AddTenantAdministration();
        builder.Services.AddInfrastructure();

        // The legacy import copies documents to the tenant's storage: the local root must be the one Api and Worker use.
        builder.Services.Configure<LocalStorageOptions>(builder.Configuration.GetSection(LocalStorageOptions.SectionName));
        builder.Services.AddCatalogPersistence(catalog);
        builder.Services.AddTenantPersistence();
        builder.Services.AddTenantDatabaseAdministration(builder.Configuration["Provisioning:AdminConnectionString"] ?? catalog);

        // With Redis, tenant lifecycle changes made here evict the tenant lookups cached by Api and Worker at once.
        if (builder.Configuration.GetConnectionString("Redis") is { Length: > 0 } redis)
        {
            builder.Services.AddRedisCache(redis);
        }

        // Jobs run here may produce messages (e.g. bus.outbox sends the pending outbox).
        if (builder.Configuration.GetConnectionString("RabbitMq") is { Length: > 0 } rabbitMq)
        {
            builder.Services.AddMessageBusClient(rabbitMq);
        }

        return builder.Build();
    }
}
