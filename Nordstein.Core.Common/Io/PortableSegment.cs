namespace Nordstein.Core.Common.Io;

/// <summary>
/// Whether one directory or file-name segment is portable: it means the same on Linux, Windows and SMB shares and
/// in every backup tool. The portable set is deliberately narrow: lower-case ASCII letters, digits, <c>_</c> and
/// <c>-</c>, no dot segment and no Windows device name. A caller that composes paths from operator input checks every
/// literal segment here before it touches the filesystem.
/// </summary>
public static class PortableSegment
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        "con", "nul", "aux", "prn",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };

    /// <summary>The first rule <paramref name="segment"/> breaks, or <see langword="null"/> when it is portable.</summary>
    /// <param name="segment">One segment, without separators.</param>
    /// <param name="maxLength">The longest segment the caller accepts.</param>
    public static PortableSegmentRule? PortableSegmentViolation(this string segment, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);
        if (segment is "." or "..")
        {
            return PortableSegmentRule.DotSegment;
        }

        if (segment.Length == 0 || segment.Length > maxLength)
        {
            return PortableSegmentRule.Length;
        }

        foreach (char character in segment)
        {
            if (!(char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '_' or '-'))
            {
                return PortableSegmentRule.Charset;
            }
        }

        return ReservedNames.Contains(segment) ? PortableSegmentRule.ReservedName : null;
    }

    /// <summary>Whether <paramref name="segment"/> breaks no rule of <see cref="PortableSegmentViolation"/>.</summary>
    public static bool IsPortableSegment(this string segment, int maxLength)
        => segment.PortableSegmentViolation(maxLength) is null;
}
