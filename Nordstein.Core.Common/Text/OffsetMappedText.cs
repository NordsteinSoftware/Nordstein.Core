namespace Nordstein.Core.Common.Text;

/// <summary>
/// A transformed text (folded, normalized, stripped) that remembers, for each of its code points,
/// the code-point offset of the original character it came from, so a range found in the
/// transformed text maps back to the exact range of the original. Built with
/// <see cref="OffsetMappedTextBuilder"/>.
/// </summary>
/// <remarks>
/// The offsets never decrease. Several code points may share one offset (one original character
/// written as several), and offsets may skip (an original character that was removed). Offsets
/// count code points of the original, as <see cref="CodePointText"/> does. Immutable and safe to
/// share between threads.
/// </remarks>
public sealed class OffsetMappedText
{
    internal OffsetMappedText(string text, int[] originalOffsets)
    {
        Text = text;
        OriginalOffsets = Array.AsReadOnly(originalOffsets);
    }

    /// <summary>
    /// Gets the transformed text.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the number of code points in <see cref="Text"/>.
    /// </summary>
    public int Length => OriginalOffsets.Count;

    /// <summary>
    /// Gets, for each code point of <see cref="Text"/>, the code-point offset of the original
    /// character it came from; never decreasing.
    /// </summary>
    public IReadOnlyList<int> OriginalOffsets { get; }

    /// <summary>
    /// Maps the non-empty half-open code-point range from <paramref name="start"/> to
    /// <paramref name="end"/> of <see cref="Text"/> to the half-open range of the original it came
    /// from: from the original of its first code point to just after the original of its last.
    /// Original characters removed inside the range are covered by it.
    /// </summary>
    /// <param name="start">The first code point, from 0 to <see cref="Length"/> − 1.</param>
    /// <param name="end">The code point after the last, from <paramref name="start"/> + 1 to <see cref="Length"/>.</param>
    /// <returns>The original half-open code-point range.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="start"/> is negative or not below <see cref="Length"/>, or <paramref name="end"/>
    /// is not after <paramref name="start"/> or beyond <see cref="Length"/>.
    /// </exception>
    public (int Start, int End) ToOriginalRange(int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(start, Length);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(end, start);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(end, Length);
        return (OriginalOffsets[start], OriginalOffsets[end - 1] + 1);
    }
}
