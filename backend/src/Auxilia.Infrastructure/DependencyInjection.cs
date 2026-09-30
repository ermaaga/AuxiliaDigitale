using Auxilia.Application.Abstractions.Channels;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Realtime;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Infrastructure.Adapters.Channels.Smtp;
using Auxilia.Infrastructure.Adapters.Templates.Fluid;
using Auxilia.Infrastructure.Caching;
using Auxilia.Infrastructure.Messaging;
using Auxilia.Infrastructure.Realtime;
using Auxilia.Infrastructure.Security;
using Auxilia.Infrastructure.Security.Tokens;
using Auxilia.Infrastructure.Tenancy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        // Replaced by AddMessageBusClient / AddMessageBusWorker when RabbitMQ is configured.
        services.TryAddSingleton<IMessageSender, UnavailableMessageSender>();

        // Outbound channel adapters (keyed by provider on the account) and the template engine (ARCHITECTURE §6, §8).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMessageChannel, SmtpEmailChannel>());
        services.TryAddSingleton<ITemplateRenderer, FluidTemplateRenderer>();

        services.TryAddSingleton<IPasswordHasher, CompositePasswordHasher>();

        // Tokens (skill auxilia-security): ES256 ring in the Catalog, JWT issuer, deny-list on the distributed cache
        // (in memory until AddRedisCache replaces IDistributedCache).
        services.AddOptions<TokenOptions>();
        services.AddDistributedMemoryCache();
        services.TryAddSingleton<SigningKeyRing>();
        services.TryAddScoped<ISigningKeyFactory, SigningKeyFactory>();
        services.TryAddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.TryAddSingleton<IAccessTokenDenyList, DistributedAccessTokenDenyList>();
        services.TryAddSingleton<ITotpService, TotpService>();

        // Replaced by AddRealtime in the Api and the Worker.
        services.TryAddScoped<IRealtimeNotifier, NullRealtimeNotifier>();

        return services;
    }
}
