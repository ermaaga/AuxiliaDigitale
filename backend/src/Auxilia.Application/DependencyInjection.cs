using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Jobs;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Bus;
using Auxilia.Application.Cases;
using Auxilia.Application.Configuration;
using Auxilia.Application.Directory;
using Auxilia.Application.Documents;
using Auxilia.Application.Engagement;
using Auxilia.Application.Execution;
using Auxilia.Application.Identity;
using Auxilia.Application.Identity.Public;
using Auxilia.Application.Jobs;
using Auxilia.Application.Localization;
using Auxilia.Application.Marketing;
using Auxilia.Application.Messaging;
using Auxilia.Application.Platform;
using Auxilia.Application.Platform.Modules;
using Auxilia.Application.Reporting;
using Auxilia.Application.Scheduling;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Auxilia.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Application base (D-26): operation runner, validators of this assembly and defaults that hosts or
    /// persistence replace (<see cref="ICurrentUser"/>, <see cref="IOperationTransactionFactory"/>).
    /// Module services are added by their module descriptors (task P1-11).
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICurrentUser, SystemCurrentUser>();
        services.TryAddScoped<IOperationTransactionFactory, NoOperationTransactionFactory>();
        services.TryAddScoped<IOperationRunner, OperationRunner>();
        services.TryAddScoped<IJobRunner, JobRunner>();

        // Message bus (skill auxilia-messaging-rebus): outbox, incoming processing; transport in Infrastructure.
        services.TryAddScoped<CorrelationContext>();
        services.TryAddScoped<ICorrelationContext>(provider => provider.GetRequiredService<CorrelationContext>());
        services.TryAddScoped<OutgoingMessageHeaders>();
        services.TryAddScoped<OutboxDispatcher>();
        services.TryAddScoped<IMessageOutbox, MessageOutbox>();
        services.TryAddScoped<IIncomingMessageProcessor, IncomingMessageProcessor>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRecurringJob, OutboxDispatchJob>());

        // Settings (ARCHITECTURE §7): the provider and manager need the Catalog (IPlatformSettingStore, secrets),
        // the tenant databases and IReferenceDataCache, registered by persistence and infrastructure.
        services.TryAddSingleton<ISettingDefinitionRegistry, SettingDefinitionRegistry>();
        services.TryAddScoped<SettingsSnapshotCache>();
        services.TryAddScoped<ISettingsProvider, SettingsProvider>();
        services.TryAddScoped<ISettingsManager, SettingsManager>();
        services.TryAddScoped<ISettingsQueryService, SettingsQueryService>();
        services.TryAddScoped<BrandingCache>();
        services.TryAddScoped<IBrandingQueryService, BrandingQueryService>();
        services.TryAddScoped<IBrandingManager, BrandingManager>();

        // Modules (ARCHITECTURE §5): registry, effective modules per tenant, navigation, catalog sync.
        services.TryAddSingleton<IModuleRegistry, ModuleRegistry>();
        services.TryAddScoped<TenantModulesCache>();
        services.TryAddScoped<IModuleAccess, ModuleAccess>();
        services.TryAddScoped<INavigationQueryService, NavigationQueryService>();
        services.TryAddScoped<IPlatformConsoleQueryService, PlatformConsoleQueryService>();
        services.TryAddScoped<IPlatformTenantManager, PlatformTenantManager>();
        services.TryAddScoped<ITenantLogManager, TenantLogManager>();
        services.TryAddScoped<ITenantLogQueryService, TenantLogQueryService>();
        services.TryAddScoped<ITenantLogLevelSync, TenantLogLevelSync>();
        services.TryAddScoped<ITenantAdministratorManager, TenantAdministratorManager>();
        services.TryAddScoped<IModuleCatalogManager, ModuleCatalogManager>();
        services.AddModules(Modules);

        services.AddValidatorsFrom(typeof(DependencyInjection).Assembly);

        return services;
    }

    /// <summary>
    /// Tenant provisioning and migrations: only for hosts that administer databases (auxctl, the Worker handling
    /// provisioning messages, <see cref="ITenantProvisioningWorkflow"/>), since they need <c>ITenantDatabaseAdmin</c> and a CREATEDB/CREATEROLE login. The Api
    /// never creates databases.
    /// </summary>
    public static IServiceCollection AddTenantAdministration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ITenantLifecycleManager, TenantLifecycleManager>();
        services.TryAddScoped<ITenantMigrationManager, TenantMigrationManager>();
        services.TryAddScoped<ITenantProvisioningWorkflow, TenantProvisioningWorkflow>();

        return services;
    }

    /// <summary>The modules of this deployment; adding a module = adding its descriptor here.</summary>
    public static IReadOnlyList<IModuleDescriptor> Modules { get; } =
    [
        new IdentityModule(), new ConfigurationModule(), new LocalizationModule(), new MessagingModule(),
        new DirectoryModule(), new CasesModule(), new SchedulingModule(), new DocumentsModule(),
        new EngagementModule(), new MarketingModule(), new ReportingModule(),
    ];

    /// <summary>
    /// Registers module descriptors with their settings and services. The same instance added twice is kept once;
    /// duplicate codes or event ranges fail when the registry is built.
    /// </summary>
    public static IServiceCollection AddModules(this IServiceCollection services, IEnumerable<IModuleDescriptor> modules)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(modules);

        foreach (var module in modules)
        {
            if (services.Any(descriptor => ReferenceEquals(descriptor.ImplementationInstance, module)))
            {
                continue;
            }

            services.AddSingleton(module);
            services.AddSettingDefinitions(module.Settings);
            module.AddServices(services);
        }

        return services;
    }

    /// <summary>
    /// Adds setting definitions to the registry; the same instance added twice is kept once, two definitions with the
    /// same key fail when the registry is built.
    /// </summary>
    public static IServiceCollection AddSettingDefinitions(this IServiceCollection services, IEnumerable<SettingDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definitions);

        foreach (var definition in definitions)
        {
            // Not TryAddEnumerable: it deduplicates by implementation type, and many definitions share SettingDefinition<bool>.
            services.AddSingleton(definition);
        }

        return services;
    }

    /// <summary>Registers every concrete <see cref="IValidator{T}"/> of the assembly as a singleton (validators are stateless).</summary>
    public static IServiceCollection AddValidatorsFrom(this IServiceCollection services, System.Reflection.Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);

        var validators = assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsGenericTypeDefinition: false })
            .SelectMany(type => type.GetInterfaces()
                .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IValidator<>))
                .Select(contract => (Contract: contract, Implementation: type)));

        foreach (var (contract, implementation) in validators)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton(contract, implementation));
        }

        return services;
    }
}
