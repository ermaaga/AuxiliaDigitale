using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Bus;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Rebus.Backoff;
using Rebus.Config;
using Rebus.Handlers;
using Rebus.Pipeline;
using Rebus.PostgreSql;
using Rebus.Retry.FailFast;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Rebus.Transport;

namespace Auxilia.Infrastructure.Messaging;

public static class MessageBusRegistration
{
    /// <summary>
    /// Schema of the Rebus tables in the Catalog database, created by the Catalog migrations (<c>Catalog_RebusTimeouts</c>,
    /// <c>Catalog_RebusMessages</c>): no host creates tables at run time (on Azure the Catalog login may not create in
    /// <c>public</c>). Rebus takes the schema separately, never in the table name.
    /// </summary>
    public const string RebusSchema = "catalog";

    /// <summary>Same as <see cref="RebusSchema"/>, kept for the Worker's timeouts.</summary>
    public const string TimeoutsSchema = RebusSchema;

    /// <summary>Deferred messages of the Worker's second-level retries (<see cref="MessagingMode.Worker"/>).</summary>
    public const string TimeoutsTable = "rebus_timeouts";

    /// <summary>
    /// Queues of <see cref="MessagingMode.InProcess"/> (Rebus PostgreSQL transport, native deferral) in
    /// <see cref="RebusSchema"/>.
    /// </summary>
    public const string InProcessQueueTable = "rebus_messages";

    /// <summary>
    /// Idle polling of the <see cref="MessagingMode.InProcess"/> queues: fast right after a message, then at most every
    /// two seconds per queue (one small query on an indexed table).
    /// </summary>
    private static readonly TimeSpan[] InProcessBackoff =
        [TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

    /// <summary>Api and auxctl: send-only bus (no input queue) with the routing table.</summary>
    public static IServiceCollection AddMessageBusClient(this IServiceCollection services, string rabbitMqConnectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(rabbitMqConnectionString);

        return services.AddSendOnlyBus(transport => transport.UseRabbitMqAsOneWayClient(rabbitMqConnectionString));
    }

    /// <summary>
    /// auxctl in <see cref="MessagingMode.InProcess"/>: send-only bus writing to the queue table of the Catalog, where
    /// the Api picks the messages up.
    /// </summary>
    public static IServiceCollection AddInProcessMessageBusClient(this IServiceCollection services, string catalogConnectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogConnectionString);

        return services.AddSendOnlyBus(transport => transport.UsePostgreSqlAsOneWayClient(catalogConnectionString, InProcessQueueTable, schemaName: RebusSchema));
    }

    /// <summary>
    /// Worker: one bus per queue of <see cref="MessageRouting.HandledQueues"/> (the first one also sends) on RabbitMQ,
    /// retries (immediate, then second-level with deferral stored in <c>catalog.rebus_timeouts</c> of the Catalog database, then
    /// <c>error</c>), the handlers of <see cref="MessageRouting.HandlerTypes"/>, caller taken from the message headers.
    /// </summary>
    public static IServiceCollection AddMessageBusWorker(
        this IServiceCollection services,
        string rabbitMqConnectionString,
        string catalogConnectionString,
        MessageBusOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(rabbitMqConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogConnectionString);

        services.AddMessageHandlers();
        services.Replace(ServiceDescriptor.Scoped<ICurrentUser>(provider => provider.GetRequiredService<MessageCurrentUser>()));

        return services.AddConsumerBuses(
            options ?? new MessageBusOptions(),
            (transport, queue) => transport.UseRabbitMq(rabbitMqConnectionString, queue),
            configure => configure.Timeouts(timeouts => timeouts.StoreInPostgres(
                new PostgresConnectionHelper(catalogConnectionString), TimeoutsTable, automaticallyCreateTables: false, schemaName: TimeoutsSchema)),
            backoff: null);
    }

    /// <summary>
    /// Api in <see cref="MessagingMode.InProcess"/> (ADR 0028): the same handlers and retry policy as the Worker, with
    /// the queues in the <c>catalog.rebus_messages</c> table of the Catalog database (created by the migration
    /// <c>Catalog_RebusMessages</c>). A message is removed from the
    /// table only when its handler has finished, so a process stopped halfway (scale to zero, restart) leaves it there
    /// for the next start; deferrals (second-level retries) are native to the transport. Parallelism by default
    /// <see cref="MessageBusOptions.InProcessDefaults"/>: the Api shares its CPU and the database connections with the
    /// requests.
    /// <para>
    /// <see cref="ICurrentUser"/> is the message caller inside a handler and <typeparamref name="THostCurrentUser"/>
    /// (the HTTP caller) everywhere else.
    /// </para>
    /// </summary>
    public static IServiceCollection AddInProcessMessageBus<THostCurrentUser>(
        this IServiceCollection services,
        string catalogConnectionString,
        MessageBusOptions? options = null)
        where THostCurrentUser : class, ICurrentUser
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogConnectionString);

        services.AddMessageHandlers();
        services.TryAddScoped<THostCurrentUser>();
        services.Replace(ServiceDescriptor.Scoped<ICurrentUser>(provider => MessageContext.Current is null
            ? provider.GetRequiredService<THostCurrentUser>()
            : provider.GetRequiredService<MessageCurrentUser>()));

        return services.AddConsumerBuses(
            options ?? MessageBusOptions.InProcessDefaults(),
            (transport, queue) => transport.UsePostgreSql(catalogConnectionString, InProcessQueueTable, queue, schemaName: RebusSchema),
            configure => configure,
            InProcessBackoff);
    }

    private static IServiceCollection AddSendOnlyBus(this IServiceCollection services, Action<StandardConfigurer<ITransport>> transport)
    {
        services.AddRebus(configure => configure
            .Transport(transport)
            .Routing(routing => MapMessages(routing.TypeBased())));
        services.Replace(ServiceDescriptor.Singleton<IMessageSender, RebusMessageSender>());
        return services;
    }

    private static void AddMessageHandlers(this IServiceCollection services)
    {
        services.TryAddScoped<MessageCurrentUser>();

        foreach (var handler in MessageRouting.HandlerTypes)
        {
            foreach (var messageType in MessageRouting.HandledMessageTypes(handler))
            {
                services.AddTransient(typeof(IHandleMessages<>).MakeGenericType(messageType), handler);
            }
        }

        foreach (var messageType in MessageRouting.MessageTypes)
        {
            services.AddTransient(
                typeof(IHandleMessages<>).MakeGenericType(typeof(IFailed<>).MakeGenericType(messageType)),
                typeof(SecondLevelRetryHandler<>).MakeGenericType(messageType));
        }
    }

    private static IServiceCollection AddConsumerBuses(
        this IServiceCollection services,
        MessageBusOptions options,
        Action<StandardConfigurer<ITransport>, string> transport,
        Func<RebusConfigurer, RebusConfigurer> extra,
        IReadOnlyList<TimeSpan>? backoff)
    {
        services.AddSingleton(options);

        var queues = MessageRouting.HandledQueues;
        for (var index = 0; index < queues.Count; index++)
        {
            var queue = queues[index];
            services.AddRebus(
                configure => extra(configure
                    .Transport(configurer => transport(configurer, queue))
                    .Routing(routing => MapMessages(routing.TypeBased()))
                    .Options(settings =>
                    {
                        settings.RetryStrategy(
                            errorQueueName: MessageRouting.ErrorQueue,
                            maxDeliveryAttempts: options.MaxDeliveryAttempts,
                            secondLevelRetriesEnabled: true);
                        settings.FailFastOn<MessageRejectedException>(_ => true);
                        settings.SetNumberOfWorkers(options.WorkersFor(queue));
                        settings.SetMaxParallelism(options.WorkersFor(queue));
                        if (backoff is not null)
                        {
                            settings.SetBackoffTimes(backoff);
                        }
                    })),
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
