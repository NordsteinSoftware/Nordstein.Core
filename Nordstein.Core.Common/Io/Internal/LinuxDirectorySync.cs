using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Nordstein.Core.Common.Io.Internal;

/// <summary>
/// <c>open(O_RDONLY | O_DIRECTORY | O_CLOEXEC)</c>, <c>fsync</c> and <c>close</c> through libc.
/// </summary>
internal static partial class LinuxDirectorySync
{
    private const int ORdOnly = 0;
    private const int OCloExec = 0x80000;
    private const int Einval = 22;
    private const int Eintr = 4;

    // O_DIRECTORY is 040000 in asm-generic (Arm, Arm64, ppc64le, s390x, riscv64, loongarch64);
    // x86 and x86-64 are the exception at 0200000.
    private static readonly int ODirectory =
        RuntimeInformation.ProcessArchitecture is Architecture.X86 or Architecture.X64 ? 0x10000 : 0x4000;

    [SupportedOSPlatform("linux")]
    internal static DirectorySyncResult Flush(string directory)
    {
        int descriptor;
        do
        {
            descriptor = Open(directory, ORdOnly | ODirectory | OCloExec);
        }
        while (descriptor < 0 && Marshal.GetLastPInvokeError() == Eintr);

        if (descriptor < 0)
        {
            throw Failure("open", Marshal.GetLastPInvokeError(), directory);
        }

        try
        {
            return Fsync(descriptor) == 0
                ? DirectorySyncResult.Flushed
                : FsyncFailure(Marshal.GetLastPInvokeError(), directory);
        }
        finally
        {
            // A failed close after a successful fsync loses nothing; the descriptor is gone either way.
            _ = Close(descriptor);
        }
    }

    /// <summary>
    /// Maps a failed <c>fsync</c>: <c>EINVAL</c> means the filesystem cannot sync directories,
    /// anything else is an IO error.
    /// </summary>
    internal static DirectorySyncResult FsyncFailure(int errno, string directory)
        => errno == Einval ? DirectorySyncResult.Unsupported : throw Failure("fsync", errno, directory);

    private static IOException Failure(string call, int errno, string directory)
        => new($"Could not flush directory '{directory}': {call} failed with errno {errno} ({Marshal.GetPInvokeErrorMessage(errno)}).");

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static partial int Fsync(int descriptor);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int Close(int descriptor);
}
