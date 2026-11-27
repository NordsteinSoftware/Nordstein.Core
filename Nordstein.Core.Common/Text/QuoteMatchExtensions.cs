namespace Nordstein.Core.Common.Text;

/// <summary>
/// Exact-once matching of a quote in a transformed text: a quotation cites a place only when it
/// occurs exactly once.
/// </summary>
public static class QuoteMatchExtensions
{
    /// <summary>
    /// Finds <paramref name="quote"/> in <paramref name="text"/>, both transformed the same way (such
    /// as with <see cref="QuoteFoldingExtensions.FoldForQuote"/>), and maps the match back to the
    /// original of <paramref name="text"/> when it occurs exactly once.
    /// </summary>
    /// <remarks>
    /// The texts are compared code point by code point (ordinal); only <see cref="OffsetMappedText.Text"/>
    /// of <paramref name="quote"/> is read. Overlapping occurrences each count. The range is
    /// <see cref="OffsetMappedText.ToOriginalRange"/> of the occurrence, in code points of the
    /// original, so original characters removed inside it are covered by it. Pure and thread-safe;
    /// linear in the text for each occurrence found.
    /// </remarks>
    /// <param name="text">The transformed text searched.</param>
    /// <param name="quote">The transformed quote; not empty.</param>
    /// <returns>The count and, for exactly one occurrence, the original range.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="quote"/> is empty: it would occur everywhere.</exception>
    public static QuoteMatch FindExactlyOnce(this OffsetMappedText text, OffsetMappedText quote)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(quote);
        if (quote.Length == 0)
        {
            throw new ArgumentException("The quote is empty, so it would occur at every position.", nameof(quote));
        }

        // Both texts are well-formed, so an ordinal hit starts and ends on a code point.
        int first = -1;
        int count = 0;
        for (int at = text.Text.IndexOf(quote.Text, StringComparison.Ordinal); at >= 0; at = text.Text.IndexOf(quote.Text, at + 1, StringComparison.Ordinal))
        {
            first = count == 0 ? at : first;
            count++;
        }

        if (count != 1)
        {
            return new QuoteMatch(count, null);
        }

        int start = CodePointsBefore(text.Text, first);
        return new QuoteMatch(1, text.ToOriginalRange(start, start + quote.Length));
    }

    private static int CodePointsBefore(string text, int utf16Index)
    {
        int codePoints = 0;
        for (int index = 0; index < utf16Index; index++)
        {
            if (!char.IsLowSurrogate(text[index]))
            {
                codePoints++;
            }
        }

        return codePoints;
    }
}
