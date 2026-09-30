namespace Auxilia.Infrastructure.Messaging;

/// <summary>Retry policy of the Worker (skill auxilia-messaging-rebus, "Reliability").</summary>
public sealed class MessageBusOptions
{
    /// <summary>Immediate attempts before the second-level retries.</summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    /// <summary>Delays of the second-level retries; after the last one the message goes to the error queue.</summary>
    public IReadOnlyList<TimeSpan> SecondLevelRetryDelays { get; set; } =
        [TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30)];

    /// <summary>Messages handled in parallel by each queue of the Worker.</summary>
    public int WorkersPerQueue { get; set; } = 2;
}
