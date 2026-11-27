namespace Nordstein.Core.Common.Io;

/// <summary>The outcome of <see cref="IDirectorySync.Flush"/>.</summary>
public enum DirectorySyncResult
{
    /// <summary>The directory was flushed to disk.</summary>
    Flushed,

    /// <summary>
    /// Nothing was flushed: the filesystem cannot sync directories, or the operating system is not
    /// Linux. New entries in the directory are not guaranteed to survive a crash.
    /// </summary>
    Unsupported,
}
