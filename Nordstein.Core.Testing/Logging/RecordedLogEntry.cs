using Microsoft.Extensions.Logging;

namespace Nordstein.Core.Testing.Logging;

/// <summary>
/// One entry a recording logger kept: everything a test needs to prove what a log line carries and
/// what it never carries.
/// </summary>
/// <remarks>Immutable and safe to read from any thread.</remarks>
public sealed class RecordedLogEntry
{
    internal RecordedLogEntry(
        string category,
        LogLevel level,
        EventId eventId,
        string message,
        Exception? exception,
        IReadOnlyList<KeyValuePair<string, object?>> properties)
    {
        Category = category;
        Level = level;
        EventId = eventId;
        Message = message;
        Exception = exception;
        Properties = properties;
    }

    /// <summary>The logger's category, for <c>ILogger&lt;T&gt;</c> the one Microsoft's logger factory derives from <c>T</c>.</summary>
    public string Category { get; }

    /// <summary>The entry's level.</summary>
    public LogLevel Level { get; }

    /// <summary>The entry's event id; <c>default</c> when none was given.</summary>
    public EventId EventId { get; }

    /// <summary>The message as the logger's formatter rendered it.</summary>
    public string Message { get; }

    /// <summary>The exception logged with the entry; null when none was.</summary>
    public Exception? Exception { get; }

    /// <summary>
    /// The structured state, copied when the entry was logged: for a message template, each
    /// placeholder's name and value in order, then <c>{OriginalFormat}</c> with the template.
    /// Empty when the state was not a sequence of name-value pairs.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, object?>> Properties { get; }

    /// <summary>The level, category and message, as in <c>Warning Category: message</c>.</summary>
    public override string ToString() => $"{Level} {Category}: {Message}";
}
