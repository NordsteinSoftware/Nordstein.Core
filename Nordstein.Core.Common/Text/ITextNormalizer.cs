namespace Nordstein.Core.Common.Text;

/// <summary>
/// Normalizes raw text (such as text extracted from a file) and maps every raw position to the
/// normalized text, so a range marked in the raw text lands on the same characters after
/// normalization. Registered as a singleton by <c>Nordstein.Core.Common.Module</c>.
/// </summary>
/// <remarks>
/// <para>
/// Raw positions are UTF-16 indices, as <see cref="string"/> indexes; normalized offsets are code
/// points, as <see cref="CodePointText"/> counts. A lone surrogate in the raw text reads as one
/// <see cref="System.Text.Rune.ReplacementChar"/>, whatever the options, so the normalized text is
/// always well-formed.
/// </para>
/// <para>
/// The map is exact for every step: a removed character, a folded ligature, a composed cluster and a
/// collapsed whitespace run each map to the normalized characters they became. See
/// <see cref="NormalizedText"/> for how a position inside one of them maps.
/// </para>
/// <para>
/// Pure and stateless: safe for any number of concurrent callers. The work is linear in the length
/// of the raw text.
/// </para>
/// </remarks>
public interface ITextNormalizer
{
    /// <summary>
    /// Normalizes <paramref name="raw"/> with every step of <see cref="TextNormalizationOptions"/>
    /// switched on.
    /// </summary>
    /// <param name="raw">The raw text; may be empty.</param>
    /// <returns>The normalized text with its offset map.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="raw"/> is <see langword="null"/>.</exception>
    NormalizedText Normalize(string raw);

    /// <summary>
    /// Normalizes <paramref name="raw"/> with the steps <paramref name="options"/> switches on.
    /// </summary>
    /// <param name="raw">The raw text; may be empty.</param>
    /// <param name="options">The steps to run.</param>
    /// <returns>The normalized text with its offset map.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    NormalizedText Normalize(string raw, TextNormalizationOptions options);
}
