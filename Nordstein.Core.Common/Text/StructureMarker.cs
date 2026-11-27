namespace Nordstein.Core.Common.Text;

/// <summary>
/// One structural boundary of a text: a half-open range of code points (as
/// <see cref="CodePointText"/> counts them) and what it is. <see cref="ITextChunker"/> and
/// <see cref="IMarkdownRenderer"/> read the same markers.
/// </summary>
/// <remarks>
/// A marker carries no text, only offsets into the text it is passed with; the consumers check that
/// every marker lies inside that text. Immutable value.
/// </remarks>
/// <param name="Kind">What the range is.</param>
/// <param name="Start">The first code point, inclusive.</param>
/// <param name="End">The code point after the last, exclusive; equal to <paramref name="Start"/> for a <see cref="StructureKind.PageBreak"/>.</param>
/// <param name="Level">
/// A heading's depth, a list item's nesting depth, a table cell's column or a page break's page
/// number (see <see cref="StructureKind"/>); 0 where it does not apply.
/// </param>
public readonly record struct StructureMarker(StructureKind Kind, int Start, int End, int Level = 0);
