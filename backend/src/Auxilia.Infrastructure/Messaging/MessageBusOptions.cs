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

    /// <summary>Queues handling more (or fewer) messages in parallel than <see cref="WorkersPerQueue"/>.</summary>
    public IReadOnlyDictionary<string, int> WorkersByQueue { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Messages handled in parallel by <paramref name="queue"/>.</summary>
    public int WorkersFor(string queue) => WorkersByQueue.TryGetValue(queue, out var workers) ? workers : WorkersPerQueue;

    /// <summary>
    /// Default of <see cref="MessagingMode.InProcess"/>: the Api shares its CPU and the Catalog connections with the
    /// requests, so one message at a time per queue, except <c>auxilia.messaging</c>, where e-mails (sign-in codes,
    /// password resets) would otherwise wait behind every delivery of a campaign.
    /// </summary>
    public static MessageBusOptions InProcessDefaults() => new()
    {
        WorkersPerQueue = 1,
        WorkersByQueue = new Dictionary<string, int>(StringComparer.Ordinal) { ["auxilia.messaging"] = 3 },
    };
}
