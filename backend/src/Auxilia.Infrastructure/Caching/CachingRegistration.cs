using Auxilia.Application.Abstractions.Caching;

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auxilia.Infrastructure.Caching;

public static class CachingRegistration
{
    public const string RedisHealthCheckName = "redis";

    /// <summary>In-memory HybridCache and <see cref="IReferenceDataCache"/>; <see cref="AddRedisCache"/> adds the L2.</summary>
    internal static IServiceCollection AddReferenceDataCache(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddHybridCache();
        services.TryAddSingleton<ICacheInvalidationBus, NullCacheInvalidationBus>();
        services.TryAddSingleton<IReferenceDataCache, HybridReferenceDataCache>();
        return services;
    }

    /// <summary>
    /// Redis/Valkey as L2 of the reference-data cache (behind a circuit breaker), the cross-node invalidation channel
    /// and a readiness check that reports Degraded while Redis is down. Connection string <c>ConnectionStrings:Redis</c>.
    /// </summary>
    public static IServiceCollection AddRedisCache(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton(new RedisConnection(connectionString));
        services.AddSingleton(provider =>
        {
            var connection = provider.GetRequiredService<RedisConnection>();
            var redis = new RedisCache(Options.Create(new RedisCacheOptions
            {
                InstanceName = "auxilia:",
                ConnectionMultiplexerFactory = connection.GetAsync,
            }));
            return new CircuitBreakingDistributedCache(
                redis, provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ILogger<CircuitBreakingDistributedCache>>());
        });
        services.Replace(ServiceDescriptor.Singleton<IDistributedCache>(provider => provider.GetRequiredService<CircuitBreakingDistributedCache>()));

        services.AddSingleton<RedisCacheInvalidationBus>();
        services.Replace(ServiceDescriptor.Singleton<ICacheInvalidationBus>(provider => provider.GetRequiredService<RedisCacheInvalidationBus>()));
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<RedisCacheInvalidationBus>());

        services.AddHealthChecks().AddCheck<RedisHealthCheck>(RedisHealthCheckName);
        services.TryAddSingleton<RedisHealthCheck>();

        return services;
    }
}
