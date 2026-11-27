using Microsoft.Extensions.Logging;
using Nordstein.Core.Testing.Logging.Internal;

namespace Nordstein.Core.Testing.Logging;

/// <summary>
/// A logger provider that keeps every enabled entry of every category it creates loggers for, in
/// write order. Register it in a test container (<c>builder.RegisterInstance(provider).As&lt;ILoggerProvider&gt;()</c>)
/// and every logger the container's logger factory creates writes into <see cref="Entries"/>.
/// </summary>
/// <remarks>
/// <para>
/// A logger factory applies its own filters before an entry reaches a provider: the container's
/// factory (<c>AddLogging</c>) drops entries below <see cref="LogLevel.Information"/> by default.
/// For lower levels, configure the factory or construct the class under test with a
/// <see cref="RecordingLogger{T}"/>.
/// </para>
/// <para>
/// Scopes are not recorded. <see cref="LogLevel.None"/> is never enabled. A formatter runs only for
/// an enabled entry, once, on the logging thread.
/// </para>
/// <para>
/// <b>Thread-safety.</b> Creating loggers, logging and reading <see cref="Entries"/> are safe from
/// any thread, concurrently. <see cref="Dispose"/> does nothing: a logger factory disposes its
/// providers with the container, and a test reads the entries afterwards.
/// </para>
/// </remarks>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly LogRecord record;

    /// <summary>
    /// Creates a provider that records every level from <see cref="LogLevel.Trace"/>.
    /// </summary>
    public RecordingLoggerProvider()
        : this(LogLevel.Trace)
    {
    }

    /// <summary>
    /// Creates a provider whose loggers are enabled from <paramref name="minimumLevel"/> up.
    /// </summary>
    /// <param name="minimumLevel">The lowest level recorded; <see cref="LogLevel.None"/> records nothing.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minimumLevel"/> is not a defined <see cref="LogLevel"/>.</exception>
    public RecordingLoggerProvider(LogLevel minimumLevel) => record = new LogRecord(minimumLevel);

    /// <summary>The lowest level the loggers record.</summary>
    public LogLevel MinimumLevel => record.MinimumLevel;

    /// <summary>A snapshot of the entries recorded so far, every category, in write order.</summary>
    public IReadOnlyList<RecordedLogEntry> Entries => record.Entries;

    /// <summary>
    /// Creates a logger that records under <paramref name="categoryName"/>.
    /// </summary>
    /// <param name="categoryName">The category every entry of the logger carries.</param>
    /// <returns>The logger.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="categoryName"/> is null.</exception>
    public ILogger CreateLogger(string categoryName)
    {
        ArgumentNullException.ThrowIfNull(categoryName);
        return new CategoryLogger(record, categoryName);
    }

    /// <summary>Does nothing: the entries stay readable, and loggers keep recording.</summary>
    public void Dispose()
    {
    }
}
