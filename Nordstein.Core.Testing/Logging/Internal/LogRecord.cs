using Microsoft.Extensions.Logging;

namespace Nordstein.Core.Testing.Logging.Internal;

/// <summary>
/// The thread-safe store behind the recording loggers: the minimum level and the entries, in
/// write order.
/// </summary>
internal sealed class LogRecord
{
    private readonly Lock gate = new();
    private readonly List<RecordedLogEntry> entries = [];

    public LogRecord(LogLevel minimumLevel)
    {
        if (minimumLevel is < LogLevel.Trace or > LogLevel.None)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumLevel), minimumLevel, "The minimum level is a defined LogLevel.");
        }

        MinimumLevel = minimumLevel;
    }

    public LogLevel MinimumLevel { get; }

    public IReadOnlyList<RecordedLogEntry> Entries
    {
        get
        {
            lock (gate)
            {
                return [.. entries];
            }
        }
    }

    public bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= MinimumLevel;

    public void Record<TState>(
        string category,
        LogLevel level,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        if (!IsEnabled(level))
        {
            return;
        }

        IReadOnlyList<KeyValuePair<string, object?>> properties =
            state is IEnumerable<KeyValuePair<string, object?>> pairs ? [.. pairs] : [];
        var entry = new RecordedLogEntry(category, level, eventId, formatter(state, exception), exception, properties);
        lock (gate)
        {
            entries.Add(entry);
        }
    }
}
