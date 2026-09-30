using System.Text.Json;

using Auxilia.Contracts.Messages;

namespace Auxilia.Application.Bus;

/// <summary>Serialization of outbox messages: only message records of <c>Auxilia.Contracts.Messages</c>.</summary>
internal static class MessageTypes
{
    private const string Namespace = "Auxilia.Contracts.Messages.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string NameOf(object message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var type = message.GetType();
        if (type.Assembly != typeof(ITenantMessage).Assembly || type.FullName is not { } name || !name.StartsWith(Namespace, StringComparison.Ordinal))
        {
            throw new ArgumentException($"{type.FullName} is not a message of {Namespace.TrimEnd('.')}.", nameof(message));
        }

        return name;
    }

    public static string Serialize(object message) => JsonSerializer.Serialize(message, message.GetType(), Json);

    public static object Deserialize(string messageType, string body)
    {
        var type = messageType.StartsWith(Namespace, StringComparison.Ordinal) ? typeof(ITenantMessage).Assembly.GetType(messageType) : null;
        return type is null
            ? throw new InvalidOperationException($"Unknown message type {messageType}.")
            : JsonSerializer.Deserialize(body, type, Json)!;
    }
}
