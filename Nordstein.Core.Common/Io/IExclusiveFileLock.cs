namespace Nordstein.Core.Common.Io;

/// <summary>
/// Takes an exclusive, process-lifetime lock on a file, for example to keep a second process
/// from writing the same data directory.
/// </summary>
/// <remarks>
/// The lock is an open <see cref="FileStream"/> with <see cref="FileShare.None"/>: on Unix .NET
/// takes <c>flock(LOCK_EX | LOCK_NB)</c> on it, on Windows the share mode refuses every other open.
/// The operating system releases the lock when the lease is disposed or the process ends, crash
/// included, so a stale lock never outlives its holder. The lock file is created when missing and
/// never deleted or written.
/// <para>
/// On Unix the lock is advisory: it stops every other <see cref="TryAcquire"/> (in this or another
/// process, on the same host) but not a process that opens the file without locking it. Setting
/// <c>DOTNET_SYSTEM_IO_DISABLEFILELOCKING</c> turns it off entirely, and network filesystems may
/// not enforce it across hosts; <see cref="IFilesystemProbe"/> reports whether it holds.
/// </para>
/// Thread-safe; registered as a singleton.
/// </remarks>
public interface IExclusiveFileLock
{
    /// <summary>
    /// Tries to lock <paramref name="path"/> without waiting.
    /// </summary>
    /// <param name="path">The lock file. Its directory must exist; the file is created when missing.</param>
    /// <returns>
    /// A lease that holds the lock until it is disposed (disposing twice is harmless), or
    /// <see langword="null"/> when another lease or process holds the lock. The caller owns the lease
    /// and must keep a reference to it: an unreachable lease is released when the garbage collector
    /// finalizes its stream.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or empty.</exception>
    /// <exception cref="DirectoryNotFoundException">The directory of <paramref name="path"/> does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">
    /// The file cannot be opened for writing, or <paramref name="path"/> is a directory.
    /// </exception>
    /// <exception cref="IOException">The file could not be opened for any other reason.</exception>
    IAsyncDisposable? TryAcquire(string path);
}
