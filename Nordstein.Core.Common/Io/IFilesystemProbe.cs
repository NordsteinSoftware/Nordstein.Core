namespace Nordstein.Core.Common.Io;

/// <summary>
/// Measures what a directory's filesystem guarantees, so a host can refuse or warn at startup
/// instead of finding out after a crash.
/// </summary>
/// <remarks>
/// The probe writes scratch files (<c>.{guid}.probe-*</c>) into the directory and deletes them
/// before it returns, on every path. The type comes from the longest mount point in
/// <c>/proc/self/mountinfo</c> that contains the directory with symbolic links resolved. A probe
/// on one host cannot see whether another host honours the same lock: treat network and FUSE
/// types as unguaranteed whatever the results say. Thread-safe; registered as a singleton.
/// </remarks>
public interface IFilesystemProbe
{
    /// <summary>
    /// Probes <paramref name="directory"/>.
    /// </summary>
    /// <param name="directory">An existing directory the process may write to.</param>
    /// <returns>The measured guarantees.</returns>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is null or empty.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="directory"/> does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">The process may not create files in it.</exception>
    /// <exception cref="IOException">
    /// A scratch file could not be written, or the directory flush failed with an error other than
    /// "unsupported".
    /// </exception>
    FilesystemCapabilities Probe(string directory);
}
