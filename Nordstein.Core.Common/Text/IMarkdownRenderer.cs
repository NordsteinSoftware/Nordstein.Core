namespace Nordstein.Core.Common.Text;

/// <summary>
/// Renders a text with its structure markers as Markdown: headings, list items, paragraphs, GitHub
/// pipe tables and page rules, with the text kept verbatim so every rendered line still matches the
/// source text. Registered as a singleton by <c>Nordstein.Core.Common.Module</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Units.</b> The <see cref="StructureKind.Heading"/>, <see cref="StructureKind.Paragraph"/>,
/// <see cref="StructureKind.ListItem"/>, <see cref="StructureKind.Table"/>,
/// <see cref="StructureKind.TableCell"/> and <see cref="StructureKind.PageBreak"/> markers, in start
/// order (at one offset the page breaks first, in the order given, then the longer marker), whatever
/// order they are passed in. A unit that starts inside one already rendered is dropped (its text outside that unit renders
/// as loose text), and so is a page break inside a rendered unit. Zero-length markers other than
/// page breaks, and markers of other kinds, are not units. Text outside every unit that is not only
/// whitespace renders trimmed, as a paragraph; a page break splits it.
/// </para>
/// <para>
/// <b>Blocks.</b> Heading: <c>#</c> × the level clamped to 1–6, a space, the text. List item: two
/// spaces × (the level clamped to 1–9, minus one), <c>- </c>, the text. Headings, list items and
/// cells write CRLF, CR and LF as one space and are trimmed; cells write <c>|</c> as <c>\|</c>.
/// Paragraph, and a table cell outside every table: verbatim. Nothing else is escaped. A unit of only
/// whitespace renders nothing. Page break: <c>---</c>, then the label block when
/// <see cref="MarkdownRenderingOptions.PageLabel"/> is set, also on an empty page.
/// </para>
/// <para>
/// <b>Tables.</b> A table's rows are the <see cref="StructureKind.TableRow"/> markers inside it, in
/// start order; a cell's are the <see cref="StructureKind.TableCell"/> markers inside the row. A row
/// that starts inside the previous row, and a cell inside the previous cell of its row, are dropped.
/// A cell's column is its level when that is after the previous cell's column, else the next column.
/// The table is as wide as its widest row, missing cells are empty, and <c>| --- |</c> follows the
/// first row. A table with no row holding a cell renders its trimmed text as a paragraph. Text inside
/// a table but outside its cells is not rendered.
/// </para>
/// <para>
/// Blocks are separated by one blank line; the result ends with one line feed, or is empty when no
/// block renders. Pure and stateless; safe for any number of concurrent callers. Rows and cells are
/// indexed once per call: O(markers log markers) plus the output.
/// </para>
/// </remarks>
public interface IMarkdownRenderer
{
    /// <summary>
    /// The highest column a <see cref="StructureKind.TableCell"/>'s level may name: the width of the
    /// widest spreadsheet grid.
    /// </summary>
    const int MaxTableColumn = 16384;

    /// <summary>
    /// Renders <paramref name="text"/> along <paramref name="markers"/> with the default
    /// <see cref="MarkdownRenderingOptions"/>.
    /// </summary>
    /// <param name="text">The text the markers point into.</param>
    /// <param name="markers">The structure markers, in code points, in any order.</param>
    /// <returns>The Markdown text.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A marker starts before 0, ends before it starts or after the text, or a table cell's level is
    /// above <see cref="MaxTableColumn"/>; the message names the marker's index, never the text.
    /// </exception>
    string Render(string text, IReadOnlyList<StructureMarker> markers);

    /// <summary>
    /// Renders <paramref name="text"/> along <paramref name="markers"/> with <paramref name="options"/>.
    /// </summary>
    /// <param name="text">The text the markers point into.</param>
    /// <param name="markers">The structure markers, in code points, in any order.</param>
    /// <param name="options">How page breaks are labelled.</param>
    /// <returns>The Markdown text.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="System.ComponentModel.DataAnnotations.ValidationException"><paramref name="options"/> is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A marker starts before 0, ends before it starts or after the text, or a table cell's level is
    /// above <see cref="MaxTableColumn"/>; the message names the marker's index, never the text.
    /// </exception>
    string Render(string text, IReadOnlyList<StructureMarker> markers, MarkdownRenderingOptions options);
}
