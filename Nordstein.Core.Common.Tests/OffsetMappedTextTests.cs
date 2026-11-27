using System.Text;
using AwesomeAssertions;
using Nordstein.Core.Common.Text;

namespace Nordstein.Core.Common.Tests;

[TestClass]
public sealed class OffsetMappedTextTests
{
    [TestMethod]
    public void Build_NothingAppended_IsEmpty()
    {
        OffsetMappedText mapped = new OffsetMappedTextBuilder().Build();

        (mapped.Text, mapped.Length, mapped.OriginalOffsets.Count).Should().Be((string.Empty, 0, 0));
    }

    [TestMethod]
    public void Build_KeepsTheTextAndOneOffsetPerCodePoint()
    {
        OffsetMappedText mapped = new OffsetMappedTextBuilder()
            .Append(new Rune('a'), 0)
            .Append(new Rune(0x1F600), 2)
            .Append(new Rune('b'), 3)
            .Build();

        (mapped.Text, mapped.Length).Should().Be(("a\U0001F600b", 3));
        mapped.OriginalOffsets.Should().Equal(0, 2, 3);
    }

    [TestMethod]
    public void Append_OneOriginalExpandsToSeveral_AllShareItsOffset()
    {
        // An ellipsis written as three dots: every dot stands for the one original character.
        OffsetMappedText mapped = new OffsetMappedTextBuilder().Append("...", 4).Build();

        mapped.OriginalOffsets.Should().Equal(4, 4, 4);
        mapped.ToOriginalRange(0, 3).Should().Be((4, 5));
    }

    [TestMethod]
    public void Append_String_CountsAnAstralCharacterOnce()
    {
        OffsetMappedText mapped = new OffsetMappedTextBuilder().Append("x\U0001F600", 1).Build();

        (mapped.Length, mapped.Text).Should().Be((2, "x\U0001F600"));
    }

    [TestMethod]
    public void Append_EmptyString_AddsNothing()
    {
        var builder = new OffsetMappedTextBuilder().Append(string.Empty, 7);

        (builder.Length, builder.Build().Text).Should().Be((0, string.Empty));
    }

    [TestMethod]
    public void ToOriginalRange_AcrossARemovedCharacter_SpansIt()
    {
        // Original "ab-cd" with the hyphen (offset 2) removed: folded "abcd".
        OffsetMappedText mapped = Map("ab-cd", skip: 2);

        mapped.ToOriginalRange(1, 3).Should().Be((1, 4));
    }

    [TestMethod]
    public void ToOriginalRange_EndsAfterTheLastMappedCodePoint()
    {
        OffsetMappedText mapped = Map("abcd", skip: -1);

        mapped.ToOriginalRange(0, 4).Should().Be((0, 4));
    }

    [TestMethod]
    [DataRow(-1, 1, "start")]
    [DataRow(2, 2, "start")]
    [DataRow(1, 1, "end")]
    [DataRow(0, 3, "end")]
    public void ToOriginalRange_EmptyOrOutside_Throws(int start, int end, string parameter)
    {
        OffsetMappedText mapped = Map("ab", skip: -1);

        Action act = () => mapped.ToOriginalRange(start, end);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(parameter);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(int.MaxValue)]
    public void Append_OffsetNoOriginalCanHave_Throws(int offset)
    {
        Action act = () => new OffsetMappedTextBuilder().Append(new Rune('a'), offset);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("originalOffset");
    }

    [TestMethod]
    public void Append_OffsetBeforeThePreviousOne_ThrowsAndKeepsTheBuilder()
    {
        var builder = new OffsetMappedTextBuilder().Append(new Rune('a'), 3);

        Action act = () => builder.Append("bc", 2);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("originalOffset");
        (builder.Length, builder.Build().Text).Should().Be((1, "a"));
    }

    [TestMethod]
    public void Append_NullString_Throws()
    {
        Action act = () => new OffsetMappedTextBuilder().Append(null!, 0); // guard test

        act.Should().Throw<ArgumentNullException>().WithParameterName("value");
    }

    [TestMethod]
    public void Build_Twice_LaterAppendsDoNotChangeTheFirstResult()
    {
        var builder = new OffsetMappedTextBuilder().Append(new Rune('a'), 0);
        OffsetMappedText first = builder.Build();

        builder.Append(new Rune('b'), 1);

        (first.Text, first.OriginalOffsets.Count, builder.Build().Text).Should().Be(("a", 1, "ab"));
    }

    [TestMethod]
    public void OriginalOffsets_CannotBeCastBackAndChanged()
    {
        OffsetMappedText mapped = Map("ab", skip: -1);

        (mapped.OriginalOffsets is int[]).Should().BeFalse();
    }

    private static OffsetMappedText Map(string original, int skip)
    {
        var builder = new OffsetMappedTextBuilder();
        int offset = 0;
        foreach (Rune rune in original.EnumerateRunes())
        {
            if (offset != skip)
            {
                builder.Append(rune, offset);
            }

            offset++;
        }

        return builder.Build();
    }
}
