namespace Nordstein.Core.Common.Text;

/// <summary>
/// The result of <see cref="ITextNormalizer.Normalize(string, TextNormalizationOptions)"/>: the
/// normalized text and, for every raw UTF-16 position, where a range that starts or ends there
/// starts or ends in the normalized text, in code points.
/// </summary>
/// <remarks>
/// <para>
/// A raw position is a UTF-16 index from 0 to <see cref="RawLength"/>; it stands between the raw
/// character before it and the one at it. Two answers exist because the characters that vanish
/// (removed characters and whitespace collapsed into a separator) belong to neither neighbour:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="StartOffsetOf"/> answers "where does a range beginning here begin": at the first
/// normalized character that comes from the raw text at or after the position, past any separator
/// a whitespace run became; <see cref="Length"/> when nothing follows.
/// </description></item>
/// <item><description>
/// <see cref="EndOffsetOf"/> answers "where does a range ending here end": just after the last
/// normalized character that comes from the raw text before the position, before any separator;
/// 0 when nothing precedes.
/// </description></item>
/// </list>
/// <para>
/// So a mapped range never starts or ends on a separator, and on a position between two kept
/// characters with nothing removed between them both answers are equal. A position inside one raw
/// character (between the two halves of a surrogate pair) or inside a grapheme cluster that
/// composition changed widens outward: a range starting there starts before the character, and a
/// range ending there ends after it, so a mapped range keeps every character the raw range
/// touched. Both answers never decrease as the position grows.
/// </para>
/// <para>
/// Immutable and safe to share between threads.
/// </para>
/// </remarks>
public sealed class NormalizedText
{
    private readonly int[] startOffsets;
    private readonly int[] endOffsets;

    internal NormalizedText(string text, int length, int[] startOffsets, int[] endOffsets)
    {
        Text = text;
        Length = length;
        this.startOffsets = startOffsets;
        this.endOffsets = endOffsets;
    }

    /// <summary>
    /// Gets the normalized text.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the number of code points in <see cref="Text"/>.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Gets the length of the raw text in UTF-16 code units: the largest raw position.
    /// </summary>
    public int RawLength => startOffsets.Length - 1;

    /// <summary>
    /// Gets the code-point offset in <see cref="Text"/> at which a range that begins at
    /// <paramref name="rawIndex"/> of the raw text begins (see the type's remarks).
    /// </summary>
    /// <param name="rawIndex">A raw UTF-16 position, from 0 to <see cref="RawLength"/>.</param>
    /// <returns>An offset from 0 to <see cref="Length"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="rawIndex"/> is negative or beyond <see cref="RawLength"/>.
    /// </exception>
    public int StartOffsetOf(int rawIndex)
    {
        CheckRawIndex(rawIndex);
        return startOffsets[rawIndex];
    }

    /// <summary>
    /// Gets the code-point offset in <see cref="Text"/> at which a range that ends at
    /// <paramref name="rawIndex"/> of the raw text ends (see the type's remarks).
    /// </summary>
    /// <param name="rawIndex">A raw UTF-16 position, from 0 to <see cref="RawLength"/>.</param>
    /// <returns>An offset from 0 to <see cref="Length"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="rawIndex"/> is negative or beyond <see cref="RawLength"/>.
    /// </exception>
    public int EndOffsetOf(int rawIndex)
    {
        CheckRawIndex(rawIndex);
        return endOffsets[rawIndex];
    }

    /// <summary>
    /// Maps the half-open raw UTF-16 range from <paramref name="rawStart"/> to
    /// <paramref name="rawEnd"/> to the half-open code-point range of <see cref="Text"/> that holds
    /// what the normalization made of it.
    /// </summary>
    /// <remarks>
    /// The result starts at <see cref="StartOffsetOf"/> of <paramref name="rawStart"/> and ends at
    /// <see cref="EndOffsetOf"/> of <paramref name="rawEnd"/>. A range whose raw characters all
    /// vanished (only whitespace or removed characters), and an empty raw range, become the empty
    /// range at <see cref="StartOffsetOf"/> of <paramref name="rawStart"/>; the result is never
    /// reversed.
    /// </remarks>
    /// <param name="rawStart">The first raw position, from 0 to <see cref="RawLength"/>.</param>
    /// <param name="rawEnd">The raw position after the last, from <paramref name="rawStart"/> to <see cref="RawLength"/>.</param>
    /// <returns>The normalized half-open code-point range; <c>Start</c> never exceeds <c>End</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="rawStart"/> is negative or beyond <see cref="RawLength"/>, or
    /// <paramref name="rawEnd"/> is before <paramref name="rawStart"/> or beyond <see cref="RawLength"/>.
    /// </exception>
    public (int Start, int End) ToNormalizedRange(int rawStart, int rawEnd)
    {
        CheckRawIndex(rawStart);
        ArgumentOutOfRangeException.ThrowIfLessThan(rawEnd, rawStart);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rawEnd, RawLength);
        int start = startOffsets[rawStart];
        return rawEnd == rawStart ? (start, start) : (start, Math.Max(start, endOffsets[rawEnd]));
    }

    private void CheckRawIndex(int rawIndex, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(rawIndex))] string parameterName = "")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rawIndex, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rawIndex, RawLength, parameterName);
    }
}
