namespace Nordstein.Core.Common.Io;

/// <summary>
/// A single-use handle for one durable write: write bytes into <see cref="Content"/>, then publish
/// to a final path. Disposing the handle without publishing aborts the write and deletes the
/// staging file.
/// </summary>
/// <remarks>
/// Not thread-safe: a handle is used by one writer. Always dispose it (an <c>await using</c> is
/// simplest) so an abandoned or failed write never leaks a staging file.
/// </remarks>
public interface IFileWriteHandle : IAsyncDisposable
{
    /// <summary>
    /// Gets the write-only stream that content is written into. It targets the staging file and is
    /// not yet durable; durability is established by a publish call. Do not dispose it directly.
    /// </summary>
    Stream Content { get; }

    /// <summary>
    /// Flushes the content to disk, then atomically publishes it as <paramref name="destinationPath"/>
    /// using create-without-replace, then flushes the containing directory (a no-op on Windows).
    /// </summary>
    /// <param name="destinationPath">The final path. Must not already exist.</param>
    /// <param name="cancellationToken">Observed while flushing.</param>
    /// <returns>A task that completes when the file is durably published.</returns>
    /// <exception cref="DestinationAlreadyExistsException"><paramref name="destinationPath"/> already exists.</exception>
    /// <exception cref="InvalidOperationException">The handle has already been published.</exception>
    Task PublishAsync(string destinationPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// As <see cref="PublishAsync"/>, but atomically replaces an existing destination. Use only
    /// where overwriting is intended.
    /// </summary>
    /// <param name="destinationPath">The final path, which may already exist.</param>
    /// <param name="cancellationToken">Observed while flushing.</param>
    /// <returns>A task that completes when the file is durably published.</returns>
    /// <exception cref="InvalidOperationException">The handle has already been published.</exception>
    Task PublishReplacingAsync(string destinationPath, CancellationToken cancellationToken = default);
}
