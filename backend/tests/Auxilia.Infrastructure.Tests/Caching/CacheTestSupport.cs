using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace Auxilia.Infrastructure.Tests.Caching;

/// <summary>Collects the event ids written by every logger of a service provider.</summary>
internal sealed class EventCollector : ILoggerProvider
{
    public ConcurrentQueue<(LogLevel Level, int EventId)> Events { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    public int Count(int eventId) => Events.Count(item => item.EventId == eventId);

    private sealed class Logger(EventCollector owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            owner.Events.Enqueue((logLevel, eventId.Id));
    }
}

/// <summary>A clock moved by hand.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}

/// <summary>An immutable cached value serialized to Redis as JSON.</summary>
public sealed record CachedPayload(string Value);
