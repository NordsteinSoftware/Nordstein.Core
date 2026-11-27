namespace Nordstein.Core.Common.Text;

/// <summary>
/// What a <see cref="StructureMarker"/> marks in a text. The numeric values are stable, so a product
/// may store them; never renumber.
/// </summary>
public enum StructureKind
{
    /// <summary>
    /// A heading; <see cref="StructureMarker.Level"/> is its depth (1 = top).
    /// </summary>
    Heading = 1,

    /// <summary>
    /// A paragraph of running text.
    /// </summary>
    Paragraph = 2,

    /// <summary>
    /// One list item; <see cref="StructureMarker.Level"/> is its nesting depth (1 = top).
    /// </summary>
    ListItem = 3,

    /// <summary>
    /// One table cell; <see cref="StructureMarker.Level"/> is its 1-based column, blank cells of its
    /// row counted, or 0 for the column after the previous cell's.
    /// </summary>
    TableCell = 4,

    /// <summary>
    /// A page boundary: zero-length, at the first code point of the page that starts there;
    /// <see cref="StructureMarker.Level"/> is that page's number.
    /// </summary>
    PageBreak = 5,

    /// <summary>
    /// A table; covers its rows. <see cref="StructureMarker.Level"/> is not used.
    /// </summary>
    Table = 6,

    /// <summary>
    /// One table row; covers its cells and the separators between them.
    /// <see cref="StructureMarker.Level"/> is not used by Core.
    /// </summary>
    TableRow = 7,
}
