namespace Nordstein.Core.Common.Io;

/// <summary>
/// Directory creation whose new entries survive a crash.
/// </summary>
public static class DirectorySyncExtensions
{
    /// <summary>
    /// Creates <paramref name="directory"/> and every missing ancestor, then flushes the parent of
    /// each directory it created, from the outermost inward, so every new entry is durable. A
    /// directory that already existed is neither created nor flushed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The path is made absolute against the current directory (<see cref="Path.GetFullPath(string)"/>,
    /// which also resolves <c>.</c> and <c>..</c>) and loses a trailing separator. The missing
    /// directories are found first, then created one at a time, outermost first; a directory another
    /// process creates in between is kept as it is (its mode too) and flushed as if this call had
    /// created it, so concurrent callers all succeed. A failure while creating throws before anything
    /// is flushed, leaving the directories created so far; a failing flush throws after all exist.
    /// </para>
    /// <para>
    /// Thread-safe as far as <paramref name="sync"/> is; <see cref="IDirectorySync"/>'s implementation is.
    /// </para>
    /// </remarks>
    /// <param name="sync">The directory flush.</param>
    /// <param name="directory">The directory to create.</param>
    /// <param name="unixMode">
    /// The mode of every created directory on Unix, filtered by the process umask as
    /// <c>mkdir(2)</c> does; <see langword="null"/> (the default) leaves it to the umask. Ignored on
    /// Windows.
    /// </param>
    /// <returns>The absolute paths of the directories this call created, outermost first; empty when <paramref name="directory"/> existed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sync"/> or <paramref name="directory"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is empty.</exception>
    /// <exception cref="IOException">A directory could not be created (a file in the way, for example) or a parent could not be flushed.</exception>
    /// <exception cref="UnauthorizedAccessException">A directory may not be created there.</exception>
    public static IReadOnlyList<string> CreateDurably(this IDirectorySync sync, string directory, UnixFileMode? unixMode = null)
    {
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentException.ThrowIfNullOrEmpty(directory);

        // Without the trailing separator, "a/b/" and its parent "a/b" would both count as missing.
        List<string> missing = [];
        for (string? current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
             current is not null && !Directory.Exists(current);
             current = Path.GetDirectoryName(current))
        {
            missing.Add(current);
        }

        if (missing.Count == 0)
        {
            return [];
        }

        // One at a time, outermost first: Directory.CreateDirectory gives a mode to the last directory only.
        missing.Reverse();
        foreach (string created in missing)
        {
            if (unixMode is { } mode && !OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(created, mode);
            }
            else
            {
                Directory.CreateDirectory(created);
            }
        }

        foreach (string created in missing)
        {
            // Only a filesystem root has no parent, and a root that is missing cannot be created.
            if (Path.GetDirectoryName(created) is { } parent)
            {
                sync.Flush(parent);
            }
        }

        return missing;
    }
}
