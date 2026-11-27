namespace Nordstein.Core.Common.Io;

/// <summary>
/// Copies a stream into a new owner-only file, computing its SHA-256 and byte count during the
/// single copy and refusing it the moment it grows past a cap.
/// </summary>
/// <remarks>
/// <para>
/// Meant for content that must not be readable by other users while it waits to be processed (an
/// upload held until it is stored, for example). On Unix a missing directory is created with mode
/// <c>0700</c> by the <c>mkdir</c> itself, and the file with mode <c>0600</c> at creation, so there is
/// no window in which either is open to others; an existing directory is never re-moded. On Windows
/// neither mode applies.
/// </para>
/// <para>
/// The file is created without replacing anything already at the path. Any failure after the file
/// was created (the cap, an I/O error, cancellation) deletes it, best effort, before the exception
/// surfaces. The file is not flushed to disk: a spool is transient by contract. Use
/// <see cref="IDurableFilePublisher"/> for a file that must survive a crash.
/// </para>
/// Thread-safe; registered as a singleton. Concurrent calls must use different paths.
/// </remarks>
public interface IPrivateFileSpool
{
    /// <summary>
    /// Copies <paramref name="source"/> from its current position to its end into a new file at
    /// <paramref name="path"/>.
    /// </summary>
    /// <param name="source">The content. It is read, never disposed or rewound.</param>
    /// <param name="path">
    /// The file to create, absolute or relative to the working directory. Missing directories on the
    /// way are created (owner-only on Unix).
    /// </param>
    /// <param name="maxBytes">The largest accepted content in bytes; zero accepts only empty content.</param>
    /// <param name="cancellationToken">Stops the copy; the partial file is deleted.</param>
    /// <returns>The SHA-256 and the length of the written content.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="path"/> is null, empty, or names a directory rather than a file (it ends in a
    /// separator or is a root).
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBytes"/> is negative.</exception>
    /// <exception cref="DestinationAlreadyExistsException">
    /// An entry already exists at <paramref name="path"/>; it is left untouched.
    /// </exception>
    /// <exception cref="SpoolLimitExceededException">
    /// The content is longer than <paramref name="maxBytes"/>; the rest of the stream is not read and
    /// no file is left.
    /// </exception>
    /// <exception cref="OperationCanceledException">The copy was cancelled; no file is left.</exception>
    /// <exception cref="IOException">The file could not be created or written, or the source failed.</exception>
    /// <exception cref="UnauthorizedAccessException">The directory or file may not be created.</exception>
    Task<SpooledContent> SpoolAsync(Stream source, string path, long maxBytes, CancellationToken cancellationToken);
}
