using System.Text;
using AwesomeAssertions;
using Nordstein.Core.Common.Text;

namespace Nordstein.Core.Common.Tests;

[TestClass]
public sealed class CodePointTextTests
{
    [TestMethod]
    public void Length_EmptyString_IsZero()
    {
        var text = new CodePointText(string.Empty);

        (text.Length, text.Count, text.Slice(0, 0)).Should().Be((0, 0, string.Empty));
    }

    [TestMethod]
    public void Indexer_AstralCharacter_CountsAsOneCodePoint()
    {
        // U+1F600 is one code point but two UTF-16 units.
        var text = new CodePointText("a\U0001F600b");

        (text.Length, text[1], text[2]).Should().Be((3, new Rune(0x1F600), new Rune('b')));
    }

    [TestMethod]
    public void Indexer_CombiningMark_IsItsOwnCodePoint()
    {
        // "u" + U+0308 COMBINING DIAERESIS: two code points, not one grapheme.
        var text = new CodePointText("ü");

        (text.Length, text[1]).Should().Be((2, new Rune(0x0308)));
    }

    [TestMethod]
    public void Indexer_LoneSurrogate_IsOneReplacementCharacter()
    {
        var text = new CodePointText("a\uD800b");

        (text.Length, text[1]).Should().Be((3, Rune.ReplacementChar));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(3)]
    public void Indexer_OutsideTheText_Throws(int index)
    {
        var text = new CodePointText("abc");

        Action act = () => _ = text[index];

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("index");
    }

    [TestMethod]
    public void Slice_AfterAnAstralCharacter_CountsItOnce()
    {
        var text = new CodePointText("a\U0001F600b");

        text.Slice(1, 3).Should().Be("\U0001F600b");
    }

    [TestMethod]
    public void Slice_KeepsCombiningMarksAsWritten()
    {
        var text = new CodePointText("Früh");

        text.Slice(2, 4).Should().Be("ü");
    }

    [TestMethod]
    public void Slice_WholeText_ReturnsTheText()
    {
        var text = new CodePointText("a\U0001F600b");

        text.Slice(0, text.Length).Should().Be("a\U0001F600b");
    }

    [TestMethod]
    public void Slice_EmptyRange_ReturnsEmpty()
        => new CodePointText("abc").Slice(3, 3).Should().BeEmpty();

    [TestMethod]
    [DataRow(-1, 1, "start")]
    [DataRow(4, 4, "start")]
    [DataRow(2, 1, "end")]
    [DataRow(0, 4, "end")]
    public void Slice_RangeOutsideTheText_Throws(int start, int end, string parameter)
    {
        var text = new CodePointText("abc");

        Action act = () => text.Slice(start, end);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(parameter);
    }

    [TestMethod]
    public void Enumeration_YieldsEveryCodePointInOrder()
    {
        var text = new CodePointText("a\U0001F600b");

        text.Should().Equal(new Rune('a'), new Rune(0x1F600), new Rune('b'));
    }

    [TestMethod]
    public void NonGenericEnumeration_YieldsEveryCodePointInOrder()
    {
        System.Collections.IEnumerable text = new CodePointText("ab");

        text.Cast<Rune>().Should().Equal(new Rune('a'), new Rune('b'));
    }

    [TestMethod]
    public void Constructor_Null_Throws()
    {
        Action act = () => _ = new CodePointText(null!); // guard test

        act.Should().Throw<ArgumentNullException>().WithParameterName("text");
    }
}
