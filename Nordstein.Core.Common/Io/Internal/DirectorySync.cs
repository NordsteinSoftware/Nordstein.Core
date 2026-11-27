namespace Nordstein.Core.Common.Io.Internal;

/// <summary>
/// Flushes directories on Linux and does nothing elsewhere. See <see cref="IDirectorySync"/>.
/// </summary>
internal sealed class DirectorySync : IDirectorySync
{
    public DirectorySyncResult Flush(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        return OperatingSystem.IsLinux() ? LinuxDirectorySync.Flush(directory) : DirectorySyncResult.Unsupported;
    }
}
