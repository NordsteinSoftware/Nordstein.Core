namespace Nordstein.Core.Common.Text;

/// <summary>
/// Which steps an <see cref="ITextNormalizer"/> runs. Every step is on by default, which suits
/// extracted text that is searched, chunked and quoted.
/// </summary>
/// <remarks>
/// The steps run in a fixed order, whatever is switched on: removal and ligature folding, then
/// composition, then whitespace collapse. Removal comes first so that a removed character between a
/// letter and its combining mark does not stop them composing, and folding comes first so that a
/// ligature followed by a combining mark composes as its letters would. Immutable.
/// </remarks>
public sealed record TextNormalizationOptions
{
    /// <summary>
    /// Gets a value indicating whether the text is composed to Unicode normalization form C (NFC),
    /// one grapheme cluster at a time. Default <see langword="true"/>.
    /// </summary>
    public bool Compose { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the Latin ligatures U+FB00 to U+FB04 (ﬀ ﬁ ﬂ ﬃ ﬄ) are written
    /// as their letters. Default <see langword="true"/>.
    /// </summary>
    public bool FoldLigatures { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the invisible characters are removed: the soft hyphen
    /// (U+00AD), the zero-width space, non-joiner and joiner (U+200B to U+200D), the word joiner
    /// (U+2060) and the byte order mark (U+FEFF). Default <see langword="true"/>.
    /// </summary>
    public bool RemoveInvisible { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the C0 and C1 control characters and DEL that are not
    /// whitespace are removed (U+0000 to U+001F, U+007F to U+009F, except tab, line feed, vertical
    /// tab, form feed, carriage return and next line). Default <see langword="true"/>.
    /// </summary>
    public bool RemoveControls { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether whitespace is collapsed: leading and trailing whitespace is
    /// removed, and every other run of whitespace becomes one space, or a paragraph break
    /// (<c>\n\n</c>) when it holds two or more line breaks. Default <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Whitespace is what <see cref="System.Text.Rune.IsWhiteSpace(System.Text.Rune)"/> says it is.
    /// A line break is a line feed, a carriage return, a carriage return followed by a line feed
    /// (one break), next line (U+0085) or the line separator (U+2028); the paragraph separator
    /// (U+2029) counts as two. Vertical tab and form feed are whitespace but no line break.
    /// </remarks>
    public bool CollapseWhitespace { get; init; } = true;
}
