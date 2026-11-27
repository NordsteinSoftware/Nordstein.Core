using AwesomeAssertions;
using Nordstein.Core.Common.Text;

namespace Nordstein.Core.Common.Tests.Text;

/// <summary>
/// Exact-once quote matching: both sides folded alike, zero or several occurrences reported with
/// their count and no range, exactly one mapped back to the original range, exact in code points.
/// </summary>
[TestClass]
public sealed class QuoteMatchTests
{
    private const string Passage = "The \u201CAnnual Summary\u201D says: travel costs were fine \u2014 revenue grew 12 %.\n\nTravel costs were fine again.";

    [TestMethod]
    public void FindExactlyOnce_ExactQuote_ReturnsItsOriginalRange()
    {
        QuoteMatch match = Passage.FoldForQuote().FindExactlyOnce("revenue grew 12 %".FoldForQuote());

        int start = Passage.IndexOf("revenue", StringComparison.Ordinal);
        match.Should().Be(new QuoteMatch(1, (start, start + "revenue grew 12 %".Length)));
    }

    [TestMethod]
    public void FindExactlyOnce_FoldedQuote_MatchesTheTypographicOriginal()
    {
        // Straight quotes, lowercase, a hyphen and collapsed spaces against curly quotes, an em dash and the original casing.
        QuoteMatch match = Passage.FoldForQuote().FindExactlyOnce("\"annual summary\" says:   travel costs were fine - revenue".FoldForQuote());

        match.OriginalRange.Should().NotBeNull();
        (int start, int end) = match.OriginalRange.GetValueOrDefault();
        Passage[start..end].Should().Be("\u201CAnnual Summary\u201D says: travel costs were fine \u2014 revenue");
    }

    [TestMethod]
    public void FindExactlyOnce_QuoteTwice_ReportsTheCountAndNoRange()
    {
        QuoteMatch match = Passage.FoldForQuote().FindExactlyOnce("travel costs were fine".FoldForQuote());

        (match.Occurrences, match.OriginalRange).Should().Be((2, null));
    }

    [TestMethod]
    public void FindExactlyOnce_QuoteNowhere_ReportsZeroAndNoRange()
    {
        QuoteMatch match = Passage.FoldForQuote().FindExactlyOnce("completely absent phrase".FoldForQuote());

        (match.Occurrences, match.OriginalRange).Should().Be((0, null));
    }

    [TestMethod]
    public void FindExactlyOnce_QuoteLongerThanTheText_ReportsZero()
    {
        "short".FoldForQuote().FindExactlyOnce("short and longer".FoldForQuote()).Occurrences.Should().Be(0);
    }

    [TestMethod]
    [DataRow("aaa", "aa", 2, DisplayName = "two overlapping")]
    [DataRow("aaaa", "aa", 3, DisplayName = "three overlapping")]
    [DataRow("abab ab", "abab", 1, DisplayName = "one, its tail a second start")]
    [DataRow("ababab", "abab", 2, DisplayName = "overlap by half")]
    public void FindExactlyOnce_OverlappingOccurrences_EachCount(string text, string quote, int expected)
    {
        text.FoldForQuote().FindExactlyOnce(quote.FoldForQuote()).Occurrences.Should().Be(expected);
    }

    [TestMethod]
    public void FindExactlyOnce_WholeText_IsTheWholeOriginal()
    {
        const string text = "  Whole text.  ";

        text.FoldForQuote().FindExactlyOnce(text.FoldForQuote()).OriginalRange.Should().Be((2, 13));
    }

    [TestMethod]
    public void FindExactlyOnce_AtTheStartAndAtTheEnd_MapToTheEdges()
    {
        OffsetMappedText text = "First words, last words".FoldForQuote();

        text.FindExactlyOnce("first".FoldForQuote()).OriginalRange.Should().Be((0, 5));
        text.FindExactlyOnce("last words".FoldForQuote()).OriginalRange.Should().Be((13, 23));
    }

    [TestMethod]
    public void FindExactlyOnce_AstralCharactersBeforeTheQuote_CountCodePoints()
    {
        const string text = "\U0001F600\U0001F600 travel costs were fine";

        text.FoldForQuote().FindExactlyOnce("travel costs were fine".FoldForQuote()).OriginalRange.Should().Be((3, 3 + 22));
    }

    [TestMethod]
    public void FindExactlyOnce_AstralCharacterEndingTheQuote_EndsJustAfterIt()
    {
        const string text = "say travel costs were fine \U0001F600 and more";

        text.FoldForQuote().FindExactlyOnce("travel costs were fine \U0001F600".FoldForQuote()).OriginalRange.Should().Be((4, 4 + 23 + 1));
    }

    [TestMethod]
    public void FindExactlyOnce_AstralCharacterEndingTheQuoteAndTheText_EndsAtTheTextsEnd()
    {
        const string text = "say travel costs were fine \U0001F600";

        text.FoldForQuote().FindExactlyOnce("travel costs were fine \U0001F600".FoldForQuote()).OriginalRange.Should().Be((4, 28));
    }

    [TestMethod]
    public void FindExactlyOnce_DotsAgainstAnEllipsis_MapToTheOneOriginal()
    {
        const string text = "wait\u2026 then";

        text.FoldForQuote().FindExactlyOnce("wait... then".FoldForQuote()).OriginalRange.Should().Be((0, text.Length));
        text.FoldForQuote().FindExactlyOnce("..".FoldForQuote()).Occurrences.Should().Be(2);
    }

    [TestMethod]
    public void FindExactlyOnce_SoftHyphenInsideTheMatch_IsCoveredByTheRange()
    {
        const string text = "a com\u00ADpound word";

        text.FoldForQuote().FindExactlyOnce("compound".FoldForQuote()).OriginalRange.Should().Be((2, 11));
    }

    [TestMethod]
    public void FindExactlyOnce_WhitespaceRunInsideTheMatch_IsCoveredByTheRange()
    {
        const string text = "one \n\t two three";

        text.FoldForQuote().FindExactlyOnce("one two".FoldForQuote()).OriginalRange.Should().Be((0, 10));
    }

    [TestMethod]
    public void FindExactlyOnce_EmptyQuote_Throws()
    {
        Action act = () => "text".FoldForQuote().FindExactlyOnce(" \u00AD ".FoldForQuote());

        act.Should().Throw<ArgumentException>().WithParameterName("quote");
    }

    [TestMethod]
    public void FindExactlyOnce_EmptyText_ReportsZero()
    {
        string.Empty.FoldForQuote().FindExactlyOnce("a".FoldForQuote()).Occurrences.Should().Be(0);
    }

    [TestMethod]
    public void FindExactlyOnce_NullArguments_Throw()
    {
        // WHY: the null-forgiving operator is banned, tests included. A default array element is a
        // genuine null the compiler types as non-null, as a mis-wired caller would pass.
        OffsetMappedText missing = (new OffsetMappedText[1])[0];
        OffsetMappedText some = "some".FoldForQuote();

        FluentActions.Invoking(() => missing.FindExactlyOnce(some)).Should().Throw<ArgumentNullException>().WithParameterName("text");
        FluentActions.Invoking(() => some.FindExactlyOnce(missing)).Should().Throw<ArgumentNullException>().WithParameterName("quote");
    }
}
