using System.Globalization;
using Nordstein.Core.Common.Validation;

namespace Nordstein.Core.Common.Text.Internal;

/// <summary>
/// The <see cref="IMarkdownRenderer"/>: one walk over the units in start order, loose text between
/// them, tables through a per-call index of rows and cells.
/// </summary>
internal sealed class MarkdownRenderer : IMarkdownRenderer
{
    private const int MaxHeadingLevel = 6;
    private const int MaxListLevel = 9;
    private const string BlockSeparator = "\n\n";
    private const string Rule = "---";

    public string Render(string text, IReadOnlyList<StructureMarker> markers) => Render(text, markers, new MarkdownRenderingOptions());

    public string Render(string text, IReadOnlyList<StructureMarker> markers, MarkdownRenderingOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(markers);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var codePoints = new CodePointText(text);
        markers.CheckWithin(codePoints.Length);
        CheckColumns(markers);

        var blocks = new List<string>();
        TableIndex? tables = null;
        int position = 0;
        foreach (StructureMarker unit in markers
                     .Where(marker => marker.Kind == StructureKind.PageBreak
                         || (marker.Kind is StructureKind.Heading or StructureKind.Paragraph or StructureKind.ListItem
                             or StructureKind.Table or StructureKind.TableCell && marker.End > marker.Start))
                     .OrderBy(marker => marker.Start)
                     .ThenBy(marker => marker.Kind == StructureKind.PageBreak ? 0 : 1)
                     .ThenByDescending(marker => marker.End))
        {
            if (unit.Start < position)
            {
                // Nested in, or overlapping, a unit already rendered.
                continue;
            }

            AddLoose(blocks, codePoints.Slice(position, unit.Start));
            if (unit.Kind == StructureKind.PageBreak)
            {
                // A break is a point: one given a length breaks at its start, and its range renders
                // as any other text.
                position = unit.Start;
                blocks.Add(Rule);
                if (options.PageLabel is { } label)
                {
                    blocks.Add(string.Concat(label, " ", unit.Level.ToString(CultureInfo.InvariantCulture)));
                }

                continue;
            }

            position = unit.End;
            string content = codePoints.Slice(unit.Start, unit.End);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            blocks.Add(unit.Kind switch
            {
                StructureKind.Heading => string.Concat(new string('#', Math.Clamp(unit.Level, 1, MaxHeadingLevel)), " ", FlattenLines(content)),
                StructureKind.ListItem => string.Concat(new string(' ', 2 * (Math.Clamp(unit.Level, 1, MaxListLevel) - 1)), "- ", FlattenLines(content)),
                StructureKind.Table => RenderTable(codePoints, tables ??= new TableIndex(markers), unit) ?? content.Trim(),
                _ => content,
            });
        }

        AddLoose(blocks, codePoints.Slice(position, codePoints.Length));
        return blocks.Count == 0 ? string.Empty : string.Concat(string.Join(BlockSeparator, blocks).TrimEnd('\n'), "\n");
    }

    private static void CheckColumns(IReadOnlyList<StructureMarker> markers)
    {
        for (int index = 0; index < markers.Count; index++)
        {
            if (markers[index] is { Kind: StructureKind.TableCell, Level: > IMarkdownRenderer.MaxTableColumn } cell)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(markers),
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"markers[{index}] is a table cell in column {cell.Level}; the last column is {IMarkdownRenderer.MaxTableColumn}."));
            }
        }
    }

    private static void AddLoose(List<string> blocks, string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            blocks.Add(text.Trim());
        }
    }

    private static string? RenderTable(CodePointText text, TableIndex tables, StructureMarker table)
    {
        var rows = new List<string?[]>();
        foreach (StructureMarker row in tables.RowsWithin(table))
        {
            rows.Add(RowCells(text, tables, row));
        }

        int columns = rows.Count == 0 ? 0 : rows.Max(row => row.Length);
        if (columns == 0)
        {
            return null;
        }

        var lines = new List<string>(rows.Count + 1);
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            string?[] cells = rows[rowIndex];
            lines.Add(FormatRow(Enumerable.Range(0, columns).Select(column => column < cells.Length ? cells[column] : null)));
            if (rowIndex == 0)
            {
                lines.Add(FormatRow(Enumerable.Repeat(Rule, columns)));
            }
        }

        return string.Join("\n", lines);
    }

    private static string?[] RowCells(CodePointText text, TableIndex tables, StructureMarker row)
    {
        var cells = new List<string?>();
        int last = 0;
        foreach (StructureMarker cell in tables.CellsWithin(row))
        {
            int column = cell.Level > last ? cell.Level : last + 1;
            while (cells.Count < column)
            {
                cells.Add(null);
            }

            cells[column - 1] = FlattenLines(text.Slice(cell.Start, cell.End)).Replace("|", "\\|", StringComparison.Ordinal);
            last = column;
        }

        return [.. cells];
    }

    private static string FormatRow(IEnumerable<string?> cells)
        => string.Concat("| ", string.Join(" | ", cells.Select(cell => cell ?? string.Empty)), " |");

    private static string FlattenLines(string text)
        => text.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

    /// <summary>
    /// The non-empty table-row and table-cell markers sorted by start, so the rows of a table and the
    /// cells of a row come from one binary search and a walk, not a scan of every marker.
    /// </summary>
    private sealed class TableIndex
    {
        private readonly StructureMarker[] rows;
        private readonly StructureMarker[] cells;

        public TableIndex(IReadOnlyList<StructureMarker> markers)
        {
            rows = Sorted(markers, StructureKind.TableRow);
            cells = Sorted(markers, StructureKind.TableCell);
        }

        public IEnumerable<StructureMarker> RowsWithin(StructureMarker table) => Within(rows, table);

        public IEnumerable<StructureMarker> CellsWithin(StructureMarker row) => Within(cells, row);

        private static StructureMarker[] Sorted(IReadOnlyList<StructureMarker> markers, StructureKind kind)
            => [.. markers.Where(marker => marker.Kind == kind && marker.End > marker.Start).OrderBy(marker => marker.Start).ThenByDescending(marker => marker.End)];

        // The markers inside the container in start order; one that starts inside the previous one taken drops.
        private static IEnumerable<StructureMarker> Within(StructureMarker[] sorted, StructureMarker container)
        {
            int low = 0;
            int high = sorted.Length;
            while (low < high)
            {
                int middle = low + ((high - low) / 2);
                if (sorted[middle].Start < container.Start)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            int covered = container.Start;
            for (int position = low; position < sorted.Length && sorted[position].Start < container.End; position++)
            {
                StructureMarker marker = sorted[position];
                if (marker.End <= container.End && marker.Start >= covered)
                {
                    covered = marker.End;
                    yield return marker;
                }
            }
        }
    }
}
