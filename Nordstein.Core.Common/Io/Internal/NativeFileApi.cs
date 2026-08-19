using System.Runtime.InteropServices;

namespace Nordstein.Core.Common.Io.Internal;

/// <summary>
/// The single native-interop seam in Nordstein.Core: flushing a directory's own entries to disk,
/// which no public BCL API exposes. Only invoked on non-Windows platforms; a no-op on Windows,
/// where NTFS metadata journaling and the rename semantics make a directory handle unnecessary.
/// </summary>
internal static partial class NativeFileApi
{
    private const int OpenReadOnly = 0; // O_RDONLY; opening a directory read-only is enough to fsync it.

    /// <summary>
    /// Flushes the directory at <paramref name="directoryPath"/> so a rename into it is durable.
    /// A no-op on Windows.
    /// </summary>
    internal static void FlushDirectory(string directoryPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        int fileDescriptor = Open(directoryPath, OpenReadOnly);
        if (fileDescriptor < 0)
        {
            throw new IOException(
                $"Unable to open directory '{directoryPath}' to flush it "
                + $"(errno {Marshal.GetLastPInvokeError()}).");
        }

        try
        {
            if (Fsync(fileDescriptor) != 0)
            {
                throw new IOException(
                    $"Unable to flush directory '{directoryPath}' "
                    + $"(errno {Marshal.GetLastPInvokeError()}).");
            }
        }
        finally
        {
            Close(fileDescriptor);
        }
    }

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Open(string pathname, int flags);

    [LibraryImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static partial int Fsync(int fileDescriptor);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int Close(int fileDescriptor);
}
