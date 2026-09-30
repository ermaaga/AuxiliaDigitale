using Auxilia.Contracts.Messages;

namespace Auxilia.Infrastructure.Messaging;

/// <summary>
/// Queues and routing by convention (skill auxilia-messaging-rebus): a message in
/// <c>Auxilia.Contracts.Messages.V&lt;n&gt;.&lt;Module&gt;</c> goes to <c>auxilia.&lt;module&gt;</c>. Poison messages end in
/// <see cref="ErrorQueue"/>.
/// </summary>
public static class MessageRouting
{
    public const string ErrorQueue = "error";

    public const string PlatformQueue = "auxilia.platform";

    /// <summary>Every message type of the contracts.</summary>
    public static IReadOnlyList<Type> MessageTypes { get; } = typeof(ITenantMessage).Assembly.GetTypes()
        .Where(type => type is { IsClass: true, IsAbstract: false, Namespace: not null }
            && type.Namespace.StartsWith("Auxilia.Contracts.Messages.V", StringComparison.Ordinal))
        .OrderBy(type => type.FullName, StringComparer.Ordinal)
        .ToArray();

    /// <summary>The queues whose messages have a handler in <paramref name="assembly"/> (the queues a Worker consumes).</summary>
    public static IReadOnlyList<string> QueuesHandledBy(System.Reflection.Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetInterfaces())
            .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(Rebus.Handlers.IHandleMessages<>))
            .Select(contract => contract.GetGenericArguments()[0])
            .Where(MessageTypes.Contains)
            .Select(QueueOf)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static string QueueOf(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        // Auxilia.Contracts.Messages.V1.Platform → auxilia.platform
        var segments = messageType.Namespace?.Split('.') ?? [];
        return segments.Length == 5 && segments[3].StartsWith('V')
            ? "auxilia." + segments[4].ToLowerInvariant()
            : throw new ArgumentException($"{messageType.FullName} is not in Auxilia.Contracts.Messages.V<n>.<Module>.", nameof(messageType));
    }
}
