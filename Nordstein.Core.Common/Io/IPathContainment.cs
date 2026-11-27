namespace Nordstein.Core.Common.Io;

/// <summary>
/// Decides whether one filesystem path may lie inside another, after resolving both to their real
/// locations, for guards of the form "this must never live under that" (a secret outside a
/// directory that is backed up, served or shared). It errs towards "within" and never answers an allow decision.
/// </summary>
/// <remarks>
/// <para>
/// Each path is read twice, and containment under any pairing counts. The first reading is the one the
/// operating system walks: the path made absolute against the current directory, <c>.</c> dropped, a
/// symbolic link (or Windows junction) that exists, dangling or not, replaced by its target, and
/// <c>..</c> applied to what is resolved so far, so <c>link/..</c> is the parent of the link's target. The
/// second is the one .NET file APIs open: <see cref="System.IO.Path.GetFullPath(string)"/> first, which
/// collapses <c>..</c> as text and normalizes Windows forms (drive-relative, trailing dots and spaces), then
/// the links in what remains. Segments below one that does not exist are kept as written. Nothing is
/// created or written.
/// </para>
/// <para>
/// The resolved paths are compared whole segment by whole segment, so <c>/x/data2</c> is not inside
/// <c>/x/data</c>. Segments are compared after Unicode normalization form C and <b>ignoring case</b>
/// (ordinal) on every operating system: a case-insensitive filesystem (NTFS, SMB, a NAS share mounted on
/// Linux) aliases <c>/x/Data</c> and <c>/x/data</c>, a normalization-insensitive one (APFS, some ZFS and
/// SMB servers) a composed and a decomposed name, and the check cannot know which filesystem a
/// not-yet-created path lands on. On a case-sensitive filesystem this reports containment for two
/// distinct directories that differ only in case or normalization: the safe side for a "must not be
/// inside" guard, the wrong one for a "must be inside" guard, so do not use this to allow access.
/// </para>
/// <para>
/// Not detected: a link inside <c>root</c> that points out of it (a tool that follows links, such as
/// <c>tar -h</c>, copies its target), bind mounts, hard links and Windows 8.3 short names. The result is a
/// point-in-time answer: a link created or swapped after the call is not seen. Implementations are
/// thread-safe and may be shared as singletons.
/// </para>
/// </remarks>
public interface IPathContainment
{
    /// <summary>
    /// Returns whether <paramref name="path"/> may be <paramref name="root"/> or lie anywhere below it,
    /// under either reading of either path.
    /// </summary>
    /// <param name="path">The path to test; absolute, or relative to the current directory.</param>
    /// <param name="root">The directory it must not be inside; absolute, or relative to the current
    /// directory. A trailing separator makes no difference.</param>
    /// <returns><c>true</c> if a resolved <paramref name="path"/> equals or descends from a resolved
    /// <paramref name="root"/>, ignoring case and normalization; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="root"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> or <paramref name="root"/> is empty, or
    /// contains a character no path may hold (U+0000).</exception>
    /// <exception cref="IOException">A link cannot be read, or more than 40 links are followed while
    /// resolving one path (a link loop).</exception>
    /// <exception cref="UnauthorizedAccessException">A directory on the way cannot be searched.</exception>
    bool MayBeWithin(string path, string root);
}
