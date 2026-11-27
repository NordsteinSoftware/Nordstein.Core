using System.Text;

namespace Nordstein.Core.Common.Text;

/// <summary>
/// The quote fold: the form in which a quotation typed by a person or a model is compared with the
/// text it quotes, so that case, spacing and typographic punctuation do not decide a match.
/// </summary>
public static class QuoteFoldingExtensions
{
    /// <summary>
    /// Folds <paramref name="text"/> for quote matching, keeping for every folded code point the
    /// code-point offset of the original character it came from, so a match in the folded text maps
    /// back to the exact original range (<see cref="OffsetMappedText.ToOriginalRange"/>).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>Every code point is lowercased by <see cref="Rune.ToLowerInvariant(Rune)"/>, a one-to-one mapping rather than full Unicode case folding (<c>ß</c> stays).</description></item>
    /// <item><description>The soft hyphen (U+00AD) is dropped.</description></item>
    /// <item><description>A run of whitespace (<see cref="Rune.IsWhiteSpace(Rune)"/>) becomes one space that maps to the character after the run; whitespace at the start and the end is dropped.</description></item>
    /// <item><description>Typographic single quotes and the prime (U+2018 to U+201B, U+2032) become <c>'</c>; double quotes, guillemets and the double prime (U+201C to U+201F, U+00AB, U+00BB, U+2033) become <c>"</c>; hyphens, dashes and the minus sign (U+2010 to U+2015, U+2212) become <c>-</c>; the ellipsis (U+2026) becomes <c>...</c>, all three dots mapping to it.</description></item>
    /// </list>
    /// <para>
    /// No other normalization happens: compose both sides first (<see cref="ITextNormalizer"/>) when
    /// decomposed text may meet composed text. A lone surrogate reads as one
    /// <see cref="Rune.ReplacementChar"/>, as in <see cref="CodePointText"/>. Pure and thread-safe.
    /// </para>
    /// </remarks>
    /// <param name="text">The text to fold.</param>
    /// <returns>The folded text with its offsets into <paramref name="text"/>, in code points.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static OffsetMappedText FoldForQuote(this string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var folded = new OffsetMappedTextBuilder();
        bool pendingSpace = false;
        int offset = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            int here = offset++;
            if (rune.Value == 0x00AD)
            {
                continue;
            }

            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = folded.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                folded.Append(new Rune(' '), here);
                pendingSpace = false;
            }

            foreach (Rune mapped in Map(rune).EnumerateRunes())
            {
                folded.Append(Rune.ToLowerInvariant(mapped), here);
            }
        }

        return folded.Build();
    }

    private static string Map(Rune rune)
        => rune.Value switch
        {
            0x2018 or 0x2019 or 0x201A or 0x201B or 0x2032 => "'",
            0x201C or 0x201D or 0x201E or 0x201F or 0x2033 or 0x00AB or 0x00BB => "\"",
            0x2010 or 0x2011 or 0x2012 or 0x2013 or 0x2014 or 0x2015 or 0x2212 => "-",
            0x2026 => "...",
            _ => rune.ToString(),
        };
}
