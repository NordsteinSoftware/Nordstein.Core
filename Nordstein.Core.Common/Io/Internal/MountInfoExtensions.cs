using System.Text;

namespace Nordstein.Core.Common.Io.Internal;

/// <summary>
/// Reads the filesystem type of a path from the text of <c>/proc/self/mountinfo</c>.
/// </summary>
internal static class MountInfoExtensions
{
    internal const string Unknown = "unknown";

    // Fields: id, parent id, major:minor, root, mount point, options, optional fields..., "-",
    // filesystem type, source, super options. The optional fields start at index 6.
    private const int MountPointField = 4;
    private const int FirstOptionalField = 6;

    /// <summary>
    /// Returns the type of the mount whose mount point is the longest whole-segment prefix of
    /// <paramref name="path"/> (an absolute, link-free path); of two mounts on one point the later,
    /// which hides the earlier, wins. Malformed lines are skipped; no match is <see cref="Unknown"/>.
    /// </summary>
    internal static string FilesystemTypeOf(this string mountInfo, string path)
    {
        string target = path.Length > 1 ? path.TrimEnd('/') : path;
        string? type = null;
        int matched = -1;
        foreach (string line in mountInfo.Split('\n'))
        {
            string[] fields = line.TrimEnd('\r').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int separator = fields.Length > FirstOptionalField ? Array.IndexOf(fields, "-", FirstOptionalField) : -1;
            if (separator < 0 || separator + 1 >= fields.Length)
            {
                continue;
            }

            string mountPoint = Unescape(fields[MountPointField]);
            if (mountPoint.Length >= matched && Contains(mountPoint, target))
            {
                type = fields[separator + 1];
                matched = mountPoint.Length;
            }
        }

        return type ?? Unknown;
    }

    private static bool Contains(string mountPoint, string path)
        => mountPoint == "/"
            || path == mountPoint
            || (path.StartsWith(mountPoint, StringComparison.Ordinal) && path[mountPoint.Length] == '/');

    // The kernel writes space, tab, newline and backslash in a mount point as \ooo octal escapes.
    private static string Unescape(string field)
    {
        if (!field.Contains('\\', StringComparison.Ordinal))
        {
            return field;
        }

        var result = new StringBuilder(field.Length);
        for (int index = 0; index < field.Length; index++)
        {
            if (field[index] == '\\' && index + 3 < field.Length && IsOctal(field, index + 1))
            {
                result.Append((char)Convert.ToInt32(field.Substring(index + 1, 3), 8));
                index += 3;
            }
            else
            {
                result.Append(field[index]);
            }
        }

        return result.ToString();
    }

    private static bool IsOctal(string field, int start)
        => field[start] is >= '0' and <= '7' && field[start + 1] is >= '0' and <= '7' && field[start + 2] is >= '0' and <= '7';
}
