using AwesomeAssertions;
using Nordstein.Core.Common.Text;

namespace Nordstein.Core.Common.Tests.Text;

/// <summary>
/// The quote fold: lowercase, one space per whitespace run, typographic punctuation to ASCII, soft
/// hyphens dropped, trimmed, and for every folded code point the offset of the original it came from.
/// </summary>
[TestClass]
public sealed class QuoteFoldingTests
{
    [TestMethod]
    [DataRow("a\u2019b", "a'b")]
    [DataRow("\u2018a\u201Ab\u201B\u2032", "'a'b''")]
    [DataRow("a\u201Cb\u201D", "a\"b\"")]
    [DataRow("\u201Ea\u201F\u2033", "\"a\"\"")]
    [DataRow("\u00ABab\u00BB", "\"ab\"")]
    [DataRow("a\u2013b\u2014c\u2212d", "a-b-c-d")]
    [DataRow("\u2010\u2011\u2012\u2015", "----")]
    [DataRow("wait\u2026", "wait...")]
    public void FoldForQuote_TypographicPunctuation_BecomesAscii(string text, string expected)
    {
        text.FoldForQuote().Text.Should().Be(expected);
    }

    [TestMethod]
    public void FoldForQuote_WhitespaceRunsAndCase_OneSpaceLowercaseMappedToTheCharacterAfterTheRun()
    {
        OffsetMappedText folded = "  Ab \t\n Cd ".FoldForQuote();

        folded.Text.Should().Be("ab cd");
        folded.OriginalOffsets.Should().Equal(2, 3, 8, 8, 9);
    }

    [TestMethod]
    public void FoldForQuote_NonAsciiCase_IsLowercasedInvariantly()
    {
        // Simple mappings only: the sharp s has no one-letter uppercase to fold, and capital sigma becomes the medial form.
        "\u00C4\u00D6\u00DC\u00DF \u03A3\u0391I".FoldForQuote().Text.Should().Be("\u00E4\u00F6\u00FC\u00DF \u03C3\u03B1i");
    }

    [TestMethod]
    public void FoldForQuote_SoftHyphen_Stripped()
    {
        OffsetMappedText folded = "ab\u00ADcd".FoldForQuote();

        folded.Text.Should().Be("abcd");
        folded.OriginalOffsets.Should().Equal(0, 1, 3, 4);
    }

    [TestMethod]
    public void FoldForQuote_SoftHyphenInsideAWhitespaceRun_KeepsOneSpace()
    {
        "a \u00AD b".FoldForQuote().Text.Should().Be("a b");
    }

    [TestMethod]
    public void FoldForQuote_Ellipsis_EveryFoldedDotMapsToTheOneOriginal()
    {
        OffsetMappedText folded = "a\u2026".FoldForQuote();

        folded.Text.Should().Be("a...");
        folded.OriginalOffsets.Should().Equal(0, 1, 1, 1);
    }

    [TestMethod]
    public void FoldForQuote_AstralCharacter_CountsAsOneOriginalCodePoint()
    {
        OffsetMappedText folded = "\U0001F600 X".FoldForQuote();

        folded.Text.Should().Be("\U0001F600 x");
        folded.OriginalOffsets.Should().Equal(0, 2, 2);
    }

    [TestMethod]
    public void FoldForQuote_LoneSurrogate_ReadsAsTheReplacementCharacter()
    {
        OffsetMappedText folded = "a\uD800b".FoldForQuote();

        folded.Text.Should().Be("a\uFFFDb");
        folded.OriginalOffsets.Should().Equal(0, 1, 2);
    }

    [TestMethod]
    [DataRow("", DisplayName = "empty")]
    [DataRow(" \t\n ", DisplayName = "only whitespace")]
    [DataRow("\u00AD \u00AD", DisplayName = "only soft hyphens and whitespace")]
    public void FoldForQuote_NothingToKeep_IsEmpty(string text)
    {
        OffsetMappedText folded = text.FoldForQuote();

        (folded.Text, folded.Length).Should().Be((string.Empty, 0));
    }

    [TestMethod]
    public void FoldForQuote_Null_Throws()
    {
        // WHY: the null-forgiving operator is banned, tests included. A default array element is a
        // genuine null the compiler types as non-null, as a mis-wired caller would pass.
        string missing = (new string[1])[0];

        Action act = () => missing.FoldForQuote();

        act.Should().Throw<ArgumentNullException>().WithParameterName("text");
    }
}
