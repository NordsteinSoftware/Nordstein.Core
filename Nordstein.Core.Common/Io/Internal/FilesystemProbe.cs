namespace Nordstein.Core.Common.Io.Internal;

/// <summary>
/// Measures a directory's guarantees with scratch files. See <see cref="IFilesystemProbe"/>.
/// </summary>
internal sealed class FilesystemProbe : IFilesystemProbe
{
    private const string MountInfoPath = "/proc/self/mountinfo";

    private readonly IDirectorySync directorySync;
    private readonly IExclusiveFileLock fileLock;
    private readonly Action<string, string> moveWithoutReplace;

    public FilesystemProbe(IDirectorySync directorySync, IExclusiveFileLock fileLock)
        : this(directorySync, fileLock, File.Move)
    {
    }

    /// <summary>
    /// Test seam: <paramref name="moveWithoutReplace"/> stands in for <see cref="File.Move(string, string)"/>.
    /// </summary>
    internal FilesystemProbe(IDirectorySync directorySync, IExclusiveFileLock fileLock, Action<string, string> moveWithoutReplace)
    {
        this.directorySync = directorySync;
        this.fileLock = fileLock;
        this.moveWithoutReplace = moveWithoutReplace;
    }

    public FilesystemCapabilities Probe(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Cannot probe '{directory}': the directory does not exist.");
        }

        return new FilesystemCapabilities
        {
            FilesystemType = FilesystemTypeOf(directory),
            RefusesReplacingRename = RefusesReplacingRename(directory),
            DirectorySync = directorySync.Flush(directory),
            RefusesSecondExclusiveOpen = RefusesSecondExclusiveOpen(directory),
        };
    }

    /// <summary>
    /// The type of the mount that holds <paramref name="directory"/> after its links are resolved,
    /// or <see cref="MountInfoExtensions.Unknown"/> off Linux or when the mount table cannot be read.
    /// </summary>
    internal string FilesystemTypeOf(string directory)
    {
        if (!OperatingSystem.IsLinux())
        {
            return MountInfoExtensions.Unknown;
        }

        string mountInfo;
        try
        {
            mountInfo = File.ReadAllText(MountInfoPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return MountInfoExtensions.Unknown;
        }

        return mountInfo.FilesystemTypeOf(LinuxRealPath.Resolve(directory));
    }

    private bool RefusesReplacingRename(string directory)
    {
        string source = ScratchPath(directory, "rename-source");
        string target = ScratchPath(directory, "rename-target");
        try
        {
            File.WriteAllBytes(source, [1]);
            File.WriteAllBytes(target, [2]);
            try
            {
                moveWithoutReplace(source, target);
            }
            catch (IOException)
            {
                // Refused, as hoped; what is left on disk decides.
            }

            return File.ReadAllBytes(target) is [2];
        }
        finally
        {
            File.Delete(source);
            File.Delete(target);
        }
    }

    private bool RefusesSecondExclusiveOpen(string directory)
    {
        string path = ScratchPath(directory, "lock");
        try
        {
            IAsyncDisposable? first = fileLock.TryAcquire(path);
            if (first is null)
            {
                return false;
            }

            try
            {
                IAsyncDisposable? second = fileLock.TryAcquire(path);
                if (second is null)
                {
                    return true;
                }

                Release(second);
                return false;
            }
            finally
            {
                Release(first);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    // The probe's contract is synchronous; a file lease's disposal completes synchronously anyway.
    private static void Release(IAsyncDisposable lease) => lease.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static string ScratchPath(string directory, string purpose)
        => Path.Combine(directory, $".{Guid.NewGuid():N}.probe-{purpose}");
}
