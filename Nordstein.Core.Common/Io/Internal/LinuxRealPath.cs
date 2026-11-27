using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Nordstein.Core.Common.Io.Internal;

/// <summary>
/// <c>realpath(3)</c> through libc: an absolute path with every symbolic link, <c>.</c> and
/// <c>..</c> resolved.
/// </summary>
internal static partial class LinuxRealPath
{
    /// <summary>
    /// Resolves <paramref name="path"/>, or returns its <see cref="Path.GetFullPath(string)"/> when
    /// <c>realpath</c> fails (a missing segment, an unsearchable directory).
    /// </summary>
    [SupportedOSPlatform("linux")]
    internal static string Resolve(string path)
    {
        IntPtr resolved = RealPath(path, IntPtr.Zero);
        if (resolved == IntPtr.Zero)
        {
            return Path.GetFullPath(path);
        }

        try
        {
            return Marshal.PtrToStringUTF8(resolved) ?? Path.GetFullPath(path);
        }
        finally
        {
            Free(resolved);
        }
    }

    [LibraryImport("libc", EntryPoint = "realpath", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr RealPath(string path, IntPtr resolved);

    [LibraryImport("libc", EntryPoint = "free")]
    private static partial void Free(IntPtr pointer);
}
