namespace Nordstein.Core.Common.Text;

/// <summary>
/// Cuts text to a length limit without leaving half a character behind.
/// </summary>
public static class TextLengthExtensions
{
    /// <summary>
    /// Returns <paramref name="value"/> itself when it holds at most <paramref name="maxLength"/>
    /// UTF-16 code units; otherwise its longest prefix that, followed by <paramref name="marker"/>,
    /// fits in <paramref name="maxLength"/>, with the marker appended.
    /// </summary>
    /// <remarks>
    /// The cut never separates a surrogate pair: a prefix that would end in a high surrogate drops
    /// it, so the result has no lone surrogate the input did not have (UTF-8 and strict encoders
    /// refuse one). Combining sequences are not kept together. Pure and thread-safe.
    /// </remarks>
    /// <param name="value">The text.</param>
    /// <param name="maxLength">The most UTF-16 code units the result holds, marker included.</param>
    /// <param name="marker">Appended when the text is cut, such as <c>…</c>; empty by default.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> or <paramref name="marker"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxLength"/> is negative, or shorter than <paramref name="marker"/> while the text must be cut
    /// (checked on every call, so a misconfigured limit fails before the first long text).
    /// </exception>
    public static string Shorten(this string value, int maxLength, string marker = "")
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, marker.Length);
        if (value.Length <= maxLength)
        {
            return value;
        }

        int keep = maxLength - marker.Length;
        if (keep > 0 && char.IsHighSurrogate(value[keep - 1]))
        {
            keep--;
        }

        return string.Concat(value.AsSpan(0, keep), marker);
    }
}
