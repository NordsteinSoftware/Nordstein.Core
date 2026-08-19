namespace Nordstein.Core.Common.Io;

/// <summary>
/// Writes a file durably: content is streamed to a staging file on the same volume, flushed to
/// disk, then atomically published to its final path, after which the containing directory is
/// flushed so the new entry survives a crash.
/// </summary>
/// <remarks>
/// A crash before publish leaves only a staging file (which the caller's recovery may sweep), never
/// a partially written final file. Implementations are thread-safe and may be shared as singletons;
/// each <see cref="IFileWriteHandle"/> is single-use and owned by the caller.
/// </remarks>
public interface IDurableFilePublisher
{
    /// <summary>
    /// Begins a durable write. The staging file is created inside
    /// <paramref name="destinationDirectory"/> so that publishing is an intra-volume rename rather
    /// than a cross-device copy; the directory must already exist.
    /// </summary>
    /// <param name="destinationDirectory">The directory the file will ultimately be published into.</param>
    /// <returns>A single-use write handle. Write to its content stream, then publish or dispose it.</returns>
    /// <exception cref="ArgumentException"><paramref name="destinationDirectory"/> is null or empty.</exception>
    /// <exception cref="StagingUnavailableException">A staging file could not be created in the directory.</exception>
    IFileWriteHandle BeginWrite(string destinationDirectory);
}
