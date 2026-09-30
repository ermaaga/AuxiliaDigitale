using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Bus;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Rebus.Config;
using Rebus.Handlers;
using Rebus.Retry.FailFast;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;

namespace Auxilia.Infrastructure.Messaging;

public static class MessageBusRegistration
{
    /// <summary>Api and auxctl: send-only bus (no input queue) with the routing table.</summary>
    public static IServiceCollection AddMessageBusClient(this IServiceCollection services, string rabbitMqConnectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(rabbitMqConnectionString);

        services.AddRebus(configure => configure
            .Transport(transport => transport.UseRabbitMqAsOneWayClient(rabbitMqConnectionString))
            .Routing(routing => MapMessages(routing.TypeBased())));
        services.Replace(ServiceDescriptor.Singleton<IMessageSender, RebusMessageSender>());
        return services;
    }

    /// <summary>
    /// Worker: one bus per queue in <paramref name="queues"/> (the first one also sends), retries (immediate, then
    /// second-level with deferral stored in <c>rebus_timeouts</c> of the Catalog database, then <c>error</c>), handlers
    /// of <typeparamref name="THandlersAssembly"/>'s assembly, caller taken from the message headers.
    /// </summary>
    public static IServiceCollection AddMessageBusWorker<THandlersAssembly>(
        this IServiceCollection services,
        string rabbitMqConnectionString,
        string catalogConnectionString,
        IReadOnlyList<string> queues,
        MessageBusOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(rabbitMqConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogConnectionString);
        ArgumentNullException.ThrowIfNull(queues);

        options ??= new MessageBusOptions();
        services.AddSingleton(options);

        services.AddScoped<MessageCurrentUser>();
        services.Replace(ServiceDescriptor.Scoped<ICurrentUser>(provider => provider.GetRequiredService<MessageCurrentUser>()));

        services.AutoRegisterHandlersFromAssemblyOf<THandlersAssembly>();
        foreach (var messageType in MessageRouting.MessageTypes)
        {
            services.AddTransient(
                typeof(IHandleMessages<>).MakeGenericType(typeof(IFailed<>).MakeGenericType(messageType)),
                typeof(SecondLevelRetryHandler<>).MakeGenericType(messageType));
        }

        for (var index = 0; index < queues.Count; index++)
        {
            var queue = queues[index];
            services.AddRebus(
                configure => configure
                    .Transport(transport => transport.UseRabbitMq(rabbitMqConnectionString, queue))
                    .Routing(routing => MapMessages(routing.TypeBased()))
                    .Timeouts(timeouts => timeouts.StoreInPostgres(catalogConnectionString, "rebus_timeouts", automaticallyCreateTables: true))
                    .Options(settings =>
                    {
                        settings.RetryStrategy(
                            errorQueueName: MessageRouting.ErrorQueue,
                            maxDeliveryAttempts: options.MaxDeliveryAttempts,
                            secondLevelRetriesEnabled: true);
                        settings.FailFastOn<MessageRejectedException>(_ => true);
                        settings.SetNumberOfWorkers(options.WorkersPerQueue);
                        settings.SetMaxParallelism(options.WorkersPerQueue);
                    }),
                isDefaultBus: index == 0,
                key: queue);
        }

        services.Replace(ServiceDescriptor.Singleton<IMessageSender, RebusMessageSender>());
        return services;
    }

    private static void MapMessages(TypeBasedRouterConfigurationExtensions.TypeBasedRouterConfigurationBuilder routing)
    {
        foreach (var messageType in MessageRouting.MessageTypes)
        {
            routing.Map(messageType, MessageRouting.QueueOf(messageType));
        }
    }
}
