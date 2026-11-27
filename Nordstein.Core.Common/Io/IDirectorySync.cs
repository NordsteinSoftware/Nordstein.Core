namespace Nordstein.Core.Common.Io;

/// <summary>
/// Flushes a directory to disk so that entries created, renamed or removed in it survive a crash.
/// </summary>
/// <remarks>
/// A file's own <c>fsync</c> makes its data durable, not its name: the new directory entry from a
/// create or rename is only durable once the containing directory is flushed too. On Linux this
/// opens the directory, calls <c>fsync</c> and closes it. On other operating systems it does nothing
/// and reports <see cref="DirectorySyncResult.Unsupported"/>. Thread-safe; registered as a singleton.
/// </remarks>
public interface IDirectorySync
{
    /// <summary>Flushes <paramref name="directory"/> to disk.</summary>
    /// <param name="directory">An existing directory.</param>
    /// <returns>
    /// <see cref="DirectorySyncResult.Flushed"/>, or <see cref="DirectorySyncResult.Unsupported"/> when
    /// the filesystem cannot sync directories (<c>EINVAL</c>) or the operating system is not Linux.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is null or empty.</exception>
    /// <exception cref="IOException">
    /// The directory could not be opened or flushed for any other reason; the message names the path
    /// and the errno.
    /// </exception>
    DirectorySyncResult Flush(string directory);
}
