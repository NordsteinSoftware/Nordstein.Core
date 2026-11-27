using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Tests.Text.Internal;
using Nordstein.Core.Common.Text;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Text;

/// <summary>
/// Markdown rendering of a text with its structure markers: units in start order, GitHub pipe tables,
/// verbatim text with only line breaks and cell pipes escaped, page rules with an optional label.
/// </summary>
[TestClass]
public sealed class MarkdownRendererTests : BaseTest<Module>
{
    private static readonly MarkdownRenderingOptions Paged = new() { PageLabel = "Page" };

    [TestMethod]
    public void Render_HeadingsListsTables_RendersExpected()
    {
        StructuredText text = new StructuredText()
            .Unit(StructureKind.Heading, 1, "Annual summary")
            .Unit(StructureKind.Heading, 2, "Revenue")
            .Unit(StructureKind.ListItem, 2, "Nested point")
            .Table(StructuredText.Row("Item", "Total"), StructuredText.Row("Cash", "12"));

        string markdown = Renderer().Render(text.Text, text.Markers);

        markdown.Should().Be(
            "# Annual summary\n\n"
            + "## Revenue\n\n"
            + "  - Nested point\n\n"
            + "| Item | Total |\n"
            + "| --- | --- |\n"
            + "| Cash | 12 |\n");
    }

    [TestMethod]
    public void Render_UnmarkedTextAndPages_RendersParagraphsAndLabelledPageRules()
    {
        StructuredText text = new StructuredText()
            .Unit(StructureKind.Paragraph, 0, "First page.")
            .PageBreak(2)
            .Loose("  Text outside every marker  ")
            .Unit(StructureKind.Paragraph, 0, "Marked.");

        string markdown = Renderer().Render(text.Text, text.Markers, Paged);

        markdown.Should().Be("First page.\n\n---\n\nPage 2\n\nText outside every marker\n\nMarked.\n");
    }

    [TestMethod]
    public void Render_PageBreakWithoutLabel_RendersTheRuleAlone()
    {
        StructuredText text = new StructuredText().Loose("One").PageBreak(2).Loose("Two");

        Renderer().Render(text.Text, text.Markers).Should().Be("One\n\n---\n\nTwo\n");
    }

    [TestMethod]
    public void Render_FirstPagesEmpty_StartsWithTheFirstPageRule()
    {
        StructuredText text = new StructuredText().PageBreak(2).PageBreak(3).Unit(StructureKind.Paragraph, 0, "Content.");

        Renderer().Render(text.Text, text.Markers, Paged).Should().Be("---\n\nPage 2\n\n---\n\nPage 3\n\nContent.\n");
    }

    [TestMethod]
    public void Render_EveryPageEmpty_RendersOnlyThePageRules()
    {
        StructuredText text = new StructuredText().PageBreak(2);

        Renderer().Render(text.Text, text.Markers, Paged).Should().Be("---\n\nPage 2\n");
    }

    [TestMethod]
    public void Render_NothingToRender_IsEmpty()
    {
        Renderer().Render(string.Empty, []).Should().BeEmpty();
        Renderer().Render(" \n\t ", [new StructureMarker(StructureKind.Paragraph, 0, 2)]).Should().BeEmpty();
    }

    [TestMethod]
    public void Render_LooseTextOnly_IsOneTrimmedParagraph()
    {
        Renderer().Render("  just text \n", []).Should().Be("just text\n");
    }

    [TestMethod]
    public void Render_TwentyThousandRowTable_RendersEveryRow()
    {
        const int rowCount = 20_000;
        StructuredText text = new StructuredText().Table([.. Enumerable.Range(0, rowCount).Select(row => StructuredText.Row($"a{row}", $"b{row}", $"c{row}"))]);

        string[] lines = Renderer().Render(text.Text, text.Markers).TrimEnd('\n').Split('\n');

        lines.Should().HaveCount(rowCount + 1);
        lines[0].Should().Be("| a0 | b0 | c0 |");
        lines[1].Should().Be("| --- | --- | --- |");
        lines[2].Should().Be("| a1 | b1 | c1 |");
        lines[^1].Should().Be($"| a{rowCount - 1} | b{rowCount - 1} | c{rowCount - 1} |");
    }

    [TestMethod]
    public void Render_LineBreaksAndCellPipes_AreEscaped()
    {
        StructuredText text = new StructuredText()
            .Unit(StructureKind.Heading, 1, "Two\r\nlines\rand\nmore")
            .Unit(StructureKind.ListItem, 1, " item\nspread ")
            .Table(StructuredText.Row("a|b", "x\ny"));

        Renderer().Render(text.Text, text.Markers).Should().Be(
            "# Two lines and more\n\n"
            + "- item spread\n\n"
            + "| a\\|b | x y |\n"
            + "| --- | --- |\n");
    }

    [TestMethod]
    public void Render_ParagraphText_StaysVerbatim()
    {
        const string content = "  *not* | escaped\nsecond line";

        Renderer().Render(content, [new StructureMarker(StructureKind.Paragraph, 0, content.Length)]).Should().Be(content + "\n");
    }

    [TestMethod]
    public void Render_SparseRow_PadsMissingColumns()
    {
        StructuredText text = new StructuredText().Table(StructuredText.Row("Name", "Qty"), [("Left", 1), ("Right", 3)]);

        Renderer().Render(text.Text, text.Markers).Should().Be(
            "| Name | Qty |  |\n"
            + "| --- | --- | --- |\n"
            + "| Left |  | Right |\n");
    }

    [TestMethod]
    public void Render_CellsWithoutOrBelowTheirColumn_FillTheNextColumn()
    {
        StructuredText text = new StructuredText().Table([("A", 0), ("B", 0), ("C", -4), ("D", 2)]);

        Renderer().Render(text.Text, text.Markers).Should().Be("| A | B | C | D |\n| --- | --- | --- | --- |\n");
    }

    [TestMethod]
    public void Render_CellInTheLastAllowedColumn_IsPadded()
    {
        StructuredText text = new StructuredText().Table([("Far", IMarkdownRenderer.MaxTableColumn)]);

        string first = Renderer().Render(text.Text, text.Markers).Split('\n')[0];

        first.Should().EndWith("|  | Far |");
        first.Count(character => character == '|').Should().Be(IMarkdownRenderer.MaxTableColumn + 1);
    }

    [TestMethod]
    public void Render_CellBeyondTheLastColumn_Throws()
    {
        StructuredText text = new StructuredText().Table([("Too far", IMarkdownRenderer.MaxTableColumn + 1)]);

        Action act = () => Renderer().Render(text.Text, text.Markers);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("markers").WithMessage("*markers[2]*");
    }

    [TestMethod]
    public void Render_CellOutsideTable_RendersVerbatimAsAParagraph()
    {
        StructuredText text = new StructuredText().Unit(StructureKind.TableCell, 1, "Stray cell");

        Renderer().Render(text.Text, text.Markers).Should().Be("Stray cell\n");
    }

    [TestMethod]
    public void Render_LevelsOutOfRange_AreClamped()
    {
        StructuredText text = new StructuredText()
            .Unit(StructureKind.Heading, 9, "Deep")
            .Unit(StructureKind.Heading, -1, "Shallow")
            .Unit(StructureKind.ListItem, 0, "Flat")
            .Unit(StructureKind.ListItem, int.MaxValue, "Deepest");

        Renderer().Render(text.Text, text.Markers).Should().Be($"###### Deep\n\n# Shallow\n\n- Flat\n\n{new string(' ', 16)}- Deepest\n");
    }

    [TestMethod]
    public void Render_TableWithoutRows_RendersItsTrimmedText()
    {
        StructuredText text = new StructuredText().Unit(StructureKind.Table, 0, "  a  b  ");

        Renderer().Render(text.Text, text.Markers).Should().Be("a  b\n");
    }

    [TestMethod]
    public void Render_TableWithRowsButNoCells_RendersItsTrimmedText()
    {
        const string content = " a b ";
        StructureMarker[] markers = [new(StructureKind.Table, 0, 5), new(StructureKind.TableRow, 0, 5)];

        Renderer().Render(content, markers).Should().Be("a b\n");
    }

    [TestMethod]
    public void Render_BlankHeadingAndListItem_RenderNothing()
    {
        StructuredText text = new StructuredText()
            .Unit(StructureKind.Heading, 1, "   ")
            .Unit(StructureKind.ListItem, 1, " ")
            .Unit(StructureKind.Paragraph, 0, "Kept.");

        Renderer().Render(text.Text, text.Markers).Should().Be("Kept.\n");
    }

    [TestMethod]
    public void Render_MarkersOutOfOrder_RenderAsInStartOrder()
    {
        StructuredText text = new StructuredText()
            .Unit(StructureKind.Heading, 1, "Title")
            .Table(StructuredText.Row("a", "b"), StructuredText.Row("c", "d"))
            .PageBreak(2)
            .Unit(StructureKind.Paragraph, 0, "Body.");

        Renderer().Render(text.Text, [.. text.Markers.Reverse()], Paged).Should().Be(Renderer().Render(text.Text, text.Markers, Paged));
    }

    [TestMethod]
    public void Render_OverlappingUnit_IsDroppedAndItsRestRendersAsLooseText()
    {
        const string content = "aaaa bbbb cccc";
        StructureMarker[] markers = [new(StructureKind.Paragraph, 0, 9), new(StructureKind.Heading, 5, 14, 1)];

        Renderer().Render(content, markers).Should().Be("aaaa bbbb\n\ncccc\n");
    }

    [TestMethod]
    public void Render_NestedUnit_IsDropped()
    {
        const string content = "aaaa bbbb";
        StructureMarker[] markers = [new(StructureKind.ListItem, 5, 9, 1), new(StructureKind.Paragraph, 0, 9)];

        Renderer().Render(content, markers).Should().Be("aaaa bbbb\n");
    }

    [TestMethod]
    public void Render_PageBreakAtAUnitsStart_ComesFirst()
    {
        const string content = "one";
        StructureMarker[] markers = [new(StructureKind.Paragraph, 0, 3), new(StructureKind.PageBreak, 0, 0, 1)];

        Renderer().Render(content, markers, Paged).Should().Be("---\n\nPage 1\n\none\n");
    }

    [TestMethod]
    public void Render_PageBreakInsideAUnit_IsDropped()
    {
        const string content = "one two";
        StructureMarker[] markers = [new(StructureKind.Paragraph, 0, 7), new(StructureKind.PageBreak, 4, 4, 2)];

        Renderer().Render(content, markers, Paged).Should().Be("one two\n");
    }

    [TestMethod]
    public void Render_ZeroLengthUnit_IsIgnoredAndDoesNotSplitLooseText()
    {
        const string content = "one two";
        StructureMarker[] markers = [new(StructureKind.Heading, 3, 3, 1), new(StructureKind.TableCell, 1, 1, 1), new(StructureKind.Table, 2, 2)];

        Renderer().Render(content, markers).Should().Be("one two\n");
    }

    [TestMethod]
    public void Render_OverlappingRowsAndCells_TheLaterOneIsDropped()
    {
        // Row [0,7) holds cells a and b; a second row starting inside it, and a cell inside cell b, drop.
        const string content = "a | bbb";
        StructureMarker[] markers =
        [
            new(StructureKind.Table, 0, 7),
            new(StructureKind.TableRow, 0, 7),
            new(StructureKind.TableRow, 4, 7),
            new(StructureKind.TableCell, 0, 1, 1),
            new(StructureKind.TableCell, 4, 7, 2),
            new(StructureKind.TableCell, 5, 6, 3),
        ];

        Renderer().Render(content, markers).Should().Be("| a | bbb |\n| --- | --- |\n");
    }

    [TestMethod]
    public void Render_RowOrCellReachingPastItsContainer_IsNotInIt()
    {
        // The second row ends after the table; the second cell of the first row ends after its row.
        const string content = "a | b\nc";
        StructureMarker[] markers =
        [
            new(StructureKind.Table, 0, 5),
            new(StructureKind.TableRow, 0, 3),
            new(StructureKind.TableRow, 4, 7),
            new(StructureKind.TableCell, 0, 1, 1),
            new(StructureKind.TableCell, 2, 5, 2),
        ];

        Renderer().Render(content, markers).Should().Be("| a |\n| --- |\n\nc\n");
    }

    [TestMethod]
    public void Render_AstralCharacters_SliceByCodePoints()
    {
        StructuredText text = new StructuredText()
            .Unit(StructureKind.Heading, 1, "\U0001F600 Title")
            .Table(StructuredText.Row("\U0001F680", "x"));

        Renderer().Render(text.Text, text.Markers).Should().Be("# \U0001F600 Title\n\n| \U0001F680 | x |\n| --- | --- |\n");
    }

    [TestMethod]
    [DataRow(-1, 2, DisplayName = "negative start")]
    [DataRow(2, 1, DisplayName = "end before start")]
    [DataRow(0, 4, DisplayName = "end after the text")]
    public void Render_MarkerOutOfRange_ThrowsNamingTheMarker(int start, int end)
    {
        StructureMarker[] markers = [new(StructureKind.Paragraph, 0, 1), new(StructureKind.Heading, start, end, 1)];

        Action act = () => Renderer().Render("abc", markers);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("markers").WithMessage("*markers[1]*");
    }

    [TestMethod]
    [DataRow("", DisplayName = "empty")]
    [DataRow("  ", DisplayName = "blank")]
    [DataRow("Page\n", DisplayName = "line feed")]
    [DataRow("Pa\rge", DisplayName = "carriage return")]
    public void Render_UnusablePageLabel_IsRejected(string label)
    {
        Action act = () => Renderer().Render("a", [], new MarkdownRenderingOptions { PageLabel = label });

        act.Should().Throw<ValidationException>().Which.ValidationResult.MemberNames.Should().Equal(nameof(MarkdownRenderingOptions.PageLabel));
    }

    [TestMethod]
    public void Render_NullArguments_Throw()
    {
        IMarkdownRenderer renderer = Renderer();

        // WHY: the null-forgiving operator is banned, tests included. A default array element is a
        // genuine null the compiler types as non-null, as a mis-wired caller would pass.
        string missingText = (new string[1])[0];
        IReadOnlyList<StructureMarker> missingMarkers = (new IReadOnlyList<StructureMarker>[1])[0];
        MarkdownRenderingOptions missingOptions = (new MarkdownRenderingOptions[1])[0];

        FluentActions.Invoking(() => renderer.Render(missingText, [])).Should().Throw<ArgumentNullException>().WithParameterName("text");
        FluentActions.Invoking(() => renderer.Render("a", missingMarkers)).Should().Throw<ArgumentNullException>().WithParameterName("markers");
        FluentActions.Invoking(() => renderer.Render("a", [], missingOptions)).Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [TestMethod]
    public void Render_ConcurrentCallers_GetTheSameMarkdown()
    {
        IMarkdownRenderer renderer = Renderer();
        StructuredText text = new StructuredText()
            .Unit(StructureKind.Heading, 1, "Title")
            .Table([.. Enumerable.Range(0, 200).Select(row => StructuredText.Row($"a{row}", $"b{row}"))])
            .PageBreak(2)
            .Loose("tail");
        string expected = renderer.Render(text.Text, text.Markers, Paged);

        var results = new string[16];
        Parallel.For(0, results.Length, worker => results[worker] = renderer.Render(text.Text, text.Markers, Paged));

        results.Should().AllBe(expected);
    }

    [TestMethod]
    public void Module_RegistersTheRendererOnce()
    {
        IServiceProvider services = GetServices();

        services.GetRequiredService<IMarkdownRenderer>().Should().BeSameAs(services.GetRequiredService<IMarkdownRenderer>());
    }

    private IMarkdownRenderer Renderer() => GetServices().GetRequiredService<IMarkdownRenderer>();
}
