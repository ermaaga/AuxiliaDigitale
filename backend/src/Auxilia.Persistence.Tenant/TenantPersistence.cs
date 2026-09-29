using Auxilia.Application.Abstractions.Persistence;

using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Persistence.Tenant;

public static class TenantPersistence
{
    /// <summary>Tenant databases through <see cref="ITenantDbContextFactory"/> only (never a DbContext in DI).</summary>
    public static IServiceCollection AddTenantPersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<TenantDataSources>();
        services.AddScoped<ITenantDbContextFactory, TenantDbContextFactory>();

        return services;
    }
}
