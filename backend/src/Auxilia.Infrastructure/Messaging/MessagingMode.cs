namespace Auxilia.Infrastructure.Messaging;

/// <summary>
/// Who consumes the queues (setting <c>Messaging:Mode</c>, ADR 0028). <see cref="Worker"/> (default): RabbitMQ, the
/// Worker consumes and the Api only sends. <see cref="InProcess"/>: the queues are a table of the Catalog database
/// (Rebus PostgreSQL transport) and the Api consumes them itself; neither RabbitMQ nor the Worker is needed.
/// </summary>
public enum MessagingMode
{
    Worker,
    InProcess,
}

public static class MessagingModeSetting
{
    public const string Key = "Messaging:Mode";

    /// <summary>Empty or missing → <see cref="MessagingMode.Worker"/>; an unknown value fails the start.</summary>
    public static MessagingMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return MessagingMode.Worker;
        }

        foreach (var mode in Enum.GetValues<MessagingMode>())
        {
            if (string.Equals(mode.ToString(), value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return mode;
            }
        }

        throw new InvalidOperationException($"{Key} must be {MessagingMode.Worker} or {MessagingMode.InProcess} (found '{value}').");
    }
}
