using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nordstein.Core.Testing.Logging.Internal;

namespace Nordstein.Core.Testing.Logging;

/// <summary>
/// An <see cref="ILogger{TCategoryName}"/> that keeps its own enabled entries, in write order: hand
/// it to the constructor of the class under test, then read <see cref="Entries"/>.
/// </summary>
/// <typeparam name="T">The category type, as for any <see cref="ILogger{TCategoryName}"/>.</typeparam>
/// <remarks>
/// Every entry carries <see cref="Category"/>, the name Microsoft's logger factory derives from
/// <typeparamref name="T"/>. Scopes are not recorded; <see cref="LogLevel.None"/> is never
/// enabled; a formatter runs only for an enabled entry. Safe for concurrent logging and reading
/// from any thread.
/// </remarks>
public sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly LogRecord record;
    private readonly CategoryLogger logger;

    /// <summary>
    /// Creates a logger that records every level from <see cref="LogLevel.Trace"/>.
    /// </summary>
    public RecordingLogger()
        : this(LogLevel.Trace)
    {
    }

    /// <summary>
    /// Creates a logger enabled from <paramref name="minimumLevel"/> up.
    /// </summary>
    /// <param name="minimumLevel">The lowest level recorded; <see cref="LogLevel.None"/> records nothing.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minimumLevel"/> is not a defined <see cref="LogLevel"/>.</exception>
    public RecordingLogger(LogLevel minimumLevel)
    {
        record = new LogRecord(minimumLevel);
        Category = CategoryOf();
        logger = new CategoryLogger(record, Category);
    }

    /// <summary>
    /// The category of every entry: the one a logger factory's <c>CreateLogger&lt;T&gt;()</c> uses
    /// (the full type name, nested types joined with <c>.</c>, without generic arguments).
    /// </summary>
    public string Category { get; }

    /// <summary>The lowest level the logger records.</summary>
    public LogLevel MinimumLevel => record.MinimumLevel;

    /// <summary>A snapshot of the entries recorded so far, in write order.</summary>
    public IReadOnlyList<RecordedLogEntry> Entries => record.Entries;

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => logger.BeginScope(state);

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => logger.IsEnabled(logLevel);

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => logger.Log(logLevel, eventId, state, exception, formatter);

    /// <summary>
    /// Asks Microsoft's own <see cref="Logger{T}"/> for its category rather than re-deriving it, so
    /// the name matches the framework's for every shape of <typeparamref name="T"/>.
    /// </summary>
    private static string CategoryOf()
    {
        using var capture = new CategoryNameCapture();
        using (var factory = new LoggerFactory([capture]))
        {
            _ = new Logger<T>(factory);
        }

        return capture.Category;
    }

    /// <summary>Keeps the one category name a logger factory asks for.</summary>
    private sealed class CategoryNameCapture : ILoggerProvider
    {
        public string Category { get; private set; } = string.Empty;

        public ILogger CreateLogger(string categoryName)
        {
            Category = categoryName;
            return NullLogger.Instance;
        }

        public void Dispose()
        {
        }
    }
}
