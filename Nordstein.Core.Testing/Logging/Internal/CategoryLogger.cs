using Microsoft.Extensions.Logging;

namespace Nordstein.Core.Testing.Logging.Internal;

/// <summary>
/// A logger of one category writing into a <see cref="LogRecord"/>.
/// </summary>
internal sealed class CategoryLogger : ILogger
{
    private readonly LogRecord record;
    private readonly string category;

    public CategoryLogger(LogRecord record, string category)
    {
        this.record = record;
        this.category = category;
    }

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
        => new NoScope();

    public bool IsEnabled(LogLevel logLevel) => record.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => record.Record(category, logLevel, eventId, state, exception, formatter);

    /// <summary>The scope handle: scopes are not recorded, so disposing it does nothing.</summary>
    private sealed class NoScope : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
