namespace Nordstein.Core.Common.Io;

/// <summary>
/// What a directory's filesystem guarantees, as measured by <see cref="IFilesystemProbe.Probe"/>.
/// </summary>
public sealed record FilesystemCapabilities
{
    /// <summary>
    /// Gets the filesystem type from <c>/proc/self/mountinfo</c> (for example <c>ext4</c>,
    /// <c>tmpfs</c>, <c>nfs4</c>, <c>fuse.sshfs</c>), or <c>unknown</c> off Linux or when no mount
    /// matches.
    /// </summary>
    public required string FilesystemType { get; init; }

    /// <summary>
    /// Gets a value indicating whether a rename without replace (<see cref="File.Move(string, string)"/>)
    /// refused an existing target and left it intact.
    /// </summary>
    public required bool RefusesReplacingRename { get; init; }

    /// <summary>
    /// Gets the result of flushing the directory through <see cref="IDirectorySync"/>.
    /// </summary>
    public required DirectorySyncResult DirectorySync { get; init; }

    /// <summary>
    /// Gets a value indicating whether <see cref="IExclusiveFileLock.TryAcquire"/> refused a second
    /// lease while the first was held.
    /// </summary>
    public required bool RefusesSecondExclusiveOpen { get; init; }
}
