using System.Text;

namespace Nordstein.Core.Common.Text;

/// <summary>
/// Builds an <see cref="OffsetMappedText"/> code point by code point, each appended with the
/// code-point offset of the original character it stands for.
/// </summary>
/// <remarks>
/// Not thread-safe: one builder belongs to one caller. <see cref="Build"/> copies, so it may be
/// called more than once and later appends do not change an earlier result.
/// </remarks>
public sealed class OffsetMappedTextBuilder
{
    private readonly StringBuilder text = new();
    private readonly List<int> offsets = [];

    /// <summary>
    /// Gets the number of code points appended so far.
    /// </summary>
    public int Length => offsets.Count;

    /// <summary>
    /// Appends <paramref name="rune"/>, which stands for the original character at
    /// <paramref name="originalOffset"/>.
    /// </summary>
    /// <param name="rune">The code point.</param>
    /// <param name="originalOffset">The original code-point offset; at least the last one appended.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="originalOffset"/> is negative, <see cref="int.MaxValue"/>, or before the last offset appended;
    /// nothing is appended.
    /// </exception>
    public OffsetMappedTextBuilder Append(Rune rune, int originalOffset)
    {
        CheckOffset(originalOffset);
        text.Append(rune.ToString());
        offsets.Add(originalOffset);
        return this;
    }

    /// <summary>
    /// Appends every code point of <paramref name="value"/>, all standing for the one original
    /// character at <paramref name="originalOffset"/> (such as an ellipsis written as three dots).
    /// A lone surrogate is appended as <see cref="Rune.ReplacementChar"/>.
    /// </summary>
    /// <param name="value">The code points; empty appends nothing.</param>
    /// <param name="originalOffset">The original code-point offset; at least the last one appended.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="originalOffset"/> is negative, <see cref="int.MaxValue"/>, or before the last offset appended;
    /// nothing is appended.
    /// </exception>
    public OffsetMappedTextBuilder Append(string value, int originalOffset)
    {
        ArgumentNullException.ThrowIfNull(value);
        CheckOffset(originalOffset);
        foreach (Rune rune in value.EnumerateRunes())
        {
            text.Append(rune.ToString());
            offsets.Add(originalOffset);
        }

        return this;
    }

    /// <summary>
    /// Returns the text built so far with its offsets.
    /// </summary>
    /// <returns>A new, immutable <see cref="OffsetMappedText"/>.</returns>
    public OffsetMappedText Build() => new(text.ToString(), [.. offsets]);

    private void CheckOffset(int originalOffset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(originalOffset);
        // No string holds int.MaxValue code points, and the end of a range after it would overflow.
        ArgumentOutOfRangeException.ThrowIfEqual(originalOffset, int.MaxValue);
        if (offsets.Count > 0)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(originalOffset, offsets[^1]);
        }
    }
}
