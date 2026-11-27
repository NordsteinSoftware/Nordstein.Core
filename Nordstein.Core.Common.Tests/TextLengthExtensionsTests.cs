using AwesomeAssertions;
using Nordstein.Core.Common.Text;

namespace Nordstein.Core.Common.Tests;

[TestClass]
public sealed class TextLengthExtensionsTests
{
    [TestMethod]
    [DataRow("", 1)]
    [DataRow("abc", 3)]
    [DataRow("abc", 10)]
    public void Shorten_TextWithinLimit_ReturnsItUnchanged(string text, int maxLength)
        => text.Shorten(maxLength, "…").Should().BeSameAs(text);

    [TestMethod]
    public void Shorten_TextOverLimit_CutsAndAppendsMarkerWithinLimit()
        => "abcdef".Shorten(4, "…").Should().Be("abc…");

    [TestMethod]
    public void Shorten_NoMarker_CutsToLimit()
        => "abcdef".Shorten(4).Should().Be("abcd");

    [TestMethod]
    public void Shorten_CutWouldSplitSurrogatePair_DropsTheWholePair()
    {
        // "ab" + U+1F600 (two UTF-16 units) + "c": a cut after 3 units would leave a lone high surrogate.
        string text = "ab\U0001F600c";

        string shortened = text.Shorten(3);

        shortened.Should().Be("ab");
        char.IsHighSurrogate(shortened[^1]).Should().BeFalse();
    }

    [TestMethod]
    public void Shorten_CutWouldSplitSurrogatePairBeforeMarker_DropsTheWholePair()
        => "ab\U0001F600cd".Shorten(4, "…").Should().Be("ab…");

    [TestMethod]
    public void Shorten_CutAfterWholePair_KeepsThePair()
        => "ab\U0001F600cd".Shorten(5, "…").Should().Be("ab\U0001F600…");

    [TestMethod]
    public void Shorten_MarkerFillsTheLimit_ReturnsTheMarker()
        => "abcdef".Shorten(1, "…").Should().Be("…");

    [TestMethod]
    public void Shorten_ZeroLimit_ReturnsEmpty()
        => "abc".Shorten(0).Should().BeEmpty();

    [TestMethod]
    public void Shorten_NullText_Throws()
    {
        string? text = null;

#pragma warning disable CS8604 // WHY: the test hands null past the annotation to prove the guard.
        Action act = () => text.Shorten(3);
#pragma warning restore CS8604

        act.Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void Shorten_NullMarker_Throws()
    {
        string? marker = null;

#pragma warning disable CS8604 // WHY: the test hands null past the annotation to prove the guard.
        Action act = () => "abc".Shorten(3, marker);
#pragma warning restore CS8604

        act.Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    [DataRow(-1, "")]
    [DataRow(0, "…")]
    [DataRow(2, "...")]
    public void Shorten_LimitBelowMarker_Throws(int maxLength, string marker)
    {
        Action act = () => "abcdef".Shorten(maxLength, marker);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
