using System.Text;
using Nordstein.Core.Common.Text;

namespace Nordstein.Core.Common.Tests.Text.Internal;

/// <summary>
/// Builds a text and its structure markers (in code points) for the chunker and renderer tests: units,
/// loose text, tables and page breaks appended in order.
/// </summary>
internal sealed class StructuredText
{
    private readonly StringBuilder text = new();
    private readonly List<StructureMarker> markers = [];
    private int position;

    public string Text => text.ToString();

    public IReadOnlyList<StructureMarker> Markers => markers;

    // The code points appended so far, counted as they are appended.
    private int Position => position;

    /// <summary>
    /// Markdown-ish blocks separated by a blank line: <c>#</c>-prefixed ones are headings of that level,
    /// the rest paragraphs.
    /// </summary>
    public static StructuredText FromBlocks(string blocks)
    {
        var built = new StructuredText();
        foreach (string block in blocks.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (built.text.Length > 0)
            {
                built.Append("\n\n");
            }

            int level = block.TakeWhile(character => character == '#').Count();
            string content = level > 0 ? block[level..].Trim() : block;
            built.Add(level > 0 ? StructureKind.Heading : StructureKind.Paragraph, level, content, separator: string.Empty);
        }

        return built;
    }

    /// <summary>
    /// One cell of a table row, with its column level.
    /// </summary>
    public static (string Text, int Level)[] Row(params string[] texts)
        => [.. texts.Select((cell, index) => (cell, index + 1))];

    public StructuredText Unit(StructureKind kind, int level, string content) => Add(kind, level, content, "\n");

    public StructuredText Loose(string content)
    {
        Append(content);
        Append("\n");
        return this;
    }

    public StructuredText PageBreak(int page)
    {
        markers.Add(new StructureMarker(StructureKind.PageBreak, Position, Position, page));
        return this;
    }

    public StructuredText Marker(StructureMarker marker)
    {
        markers.Add(marker);
        return this;
    }

    public StructuredText Table(params (string Text, int Level)[][] rows)
    {
        int tableStart = Position;
        var rowMarkers = new List<StructureMarker>();
        var cellMarkers = new List<StructureMarker>();
        for (int row = 0; row < rows.Length; row++)
        {
            int rowStart = Position;
            for (int cell = 0; cell < rows[row].Length; cell++)
            {
                if (cell > 0)
                {
                    Append(" | ");
                }

                int cellStart = Position;
                Append(rows[row][cell].Text);
                cellMarkers.Add(new StructureMarker(StructureKind.TableCell, cellStart, Position, rows[row][cell].Level));
            }

            rowMarkers.Add(new StructureMarker(StructureKind.TableRow, rowStart, Position));
            if (row + 1 < rows.Length)
            {
                Append("\n");
            }
        }

        markers.Add(new StructureMarker(StructureKind.Table, tableStart, Position));
        markers.AddRange(rowMarkers);
        markers.AddRange(cellMarkers);
        Append("\n");
        return this;
    }

    private StructuredText Add(StructureKind kind, int level, string content, string separator)
    {
        int start = Position;
        Append(content);
        markers.Add(new StructureMarker(kind, start, Position, level));
        Append(separator);
        return this;
    }

    private void Append(string value)
    {
        text.Append(value);
        position += value.EnumerateRunes().Count();
    }
}
