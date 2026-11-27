using System.Globalization;
using System.Text;

namespace Nordstein.Core.Common.Text.Internal;

/// <summary>
/// The checks the chunker and the renderer share: every marker lies inside the text it came with,
/// and which code points are whitespace.
/// </summary>
internal static class StructureMarkerChecks
{
    /// <summary>
    /// Throws for the first marker that starts before 0, ends before it starts or ends after
    /// <paramref name="length"/>; the message names the marker's index and range, never the text.
    /// </summary>
    public static void CheckWithin(this IReadOnlyList<StructureMarker> markers, int length)
    {
        for (int index = 0; index < markers.Count; index++)
        {
            StructureMarker marker = markers[index];
            if (marker.Start < 0 || marker.End < marker.Start || marker.End > length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(markers),
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"markers[{index}] [{marker.Start},{marker.End}) does not lie inside the text of {length} code points."));
            }
        }
    }

    public static bool IsWhiteSpace(this CodePointText text, int index) => Rune.IsWhiteSpace(text[index]);

    /// <summary>
    /// Narrows the half-open range to its first and last code point that is not whitespace; an empty
    /// range when it holds only whitespace.
    /// </summary>
    public static (int Start, int End) Trim(this CodePointText text, int start, int end)
    {
        while (start < end && text.IsWhiteSpace(start))
        {
            start++;
        }

        while (end > start && text.IsWhiteSpace(end - 1))
        {
            end--;
        }

        return (start, end);
    }
}
