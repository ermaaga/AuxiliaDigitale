using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Infrastructure.Caching;
using Auxilia.Infrastructure.Tenancy;

using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Technical services shared by every host (adapters are added by later tasks); the reference-data cache is in memory until <see cref="CachingRegistration.AddRedisCache"/>.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantContextSetter>(provider => provider.GetRequiredService<TenantContext>());

        services.AddReferenceDataCache();

        return services;
    }
}
