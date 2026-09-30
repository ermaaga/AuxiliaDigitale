using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Tests.Execution;

/// <summary>Captures log entries and the scope state active when each entry was written.</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly Stack<object> scopes = new();

    public List<Entry> Entries { get; } = [];

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        scopes.Push(state);
        return new PopScope(scopes);
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var properties = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
        // Innermost scope first (stack order): its value wins when nested operations share a key.
        var scope = new Dictionary<string, object?>();
        foreach (var item in scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(item => item))
        {
            scope.TryAdd(item.Key, item.Value);
        }

        Entries.Add(new Entry(logLevel, eventId, formatter(state, exception), exception,
            properties.ToDictionary(item => item.Key, item => item.Value), scope));
    }

    public sealed record Entry(
        LogLevel Level,
        EventId EventId,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties,
        IReadOnlyDictionary<string, object?> Scope);

    private sealed class PopScope(Stack<object> scopes) : IDisposable
    {
        public void Dispose() => scopes.Pop();
    }
}
