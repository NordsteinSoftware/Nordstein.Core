namespace Nordstein.Core.Common.Io.Internal;

/// <summary>
/// Tells "another holder has the lock" apart from every other failure to open a lock file.
/// </summary>
internal static class HeldLockExtensions
{
    // Unix: .NET throws a plain IOException whose HResult is the raw errno of the failed
    // flock(LOCK_NB): EWOULDBLOCK is 11 on Linux and 35 on macOS and FreeBSD.
    private const int LinuxEwouldblock = 11;
    private const int BsdEwouldblock = 35;

    // Windows: ERROR_SHARING_VIOLATION and ERROR_LOCK_VIOLATION as HRESULTs.
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);

    internal static bool IsHeldLock(this IOException exception)
        => exception.GetType() == typeof(IOException) && (OperatingSystem.IsWindows()
            ? exception.HResult is SharingViolation or LockViolation
            : exception.HResult == (OperatingSystem.IsLinux() ? LinuxEwouldblock : BsdEwouldblock));
}
