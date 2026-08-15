using AwesomeAssertions;
using Nordstein.Core.AI.Messages;

namespace Nordstein.Core.AI.Tests;

/// <summary>
/// Complements <c>ToolRequestMatchTests</c> by driving the canonical-JSON writer's remaining
/// branches: nested objects, arrays (which are order-sensitive), non-decimal numbers that keep
/// their raw text, boolean/null scalars, whitespace-only arguments, and the mixed JSON/plain-text
/// fallback. Each pins a distinct path through <c>WriteCanonical</c>/<c>ArgumentsMatch</c>.
/// </summary>
[TestClass]
public sealed class ToolRequestMatchCanonicalTests
{
    [TestMethod]
    public void Matches_NestedObjectsWithReorderedKeys_AreEqual()
    {
        var expected = new ToolRequest("a", "configure", """{"outer":{"b":2,"a":1}}""");
        var actual = new ToolRequest("b", "configure", """{"outer":{"a":1,"b":2}}""");

        ToolRequestMatch.Matches(expected, actual).Should().BeTrue();
    }

    [TestMethod]
    public void Matches_ArraysInSameOrder_AreEqual()
    {
        var expected = new ToolRequest("a", "batch", """{"ids":[1,2,3]}""");
        var actual = new ToolRequest("b", "batch", "{\n \"ids\": [1, 2, 3]\n}");

        ToolRequestMatch.Matches(expected, actual).Should().BeTrue();
    }

    [TestMethod]
    public void Matches_ArraysInDifferentOrder_AreNotEqual()
    {
        // Arrays are ordered; the canonical form preserves element order, so a reordering differs.
        var expected = new ToolRequest("a", "batch", """{"ids":[1,2,3]}""");
        var actual = new ToolRequest("b", "batch", """{"ids":[3,2,1]}""");

        ToolRequestMatch.Matches(expected, actual).Should().BeFalse();
    }

    [TestMethod]
    public void Matches_BooleanAndNullScalars_AreEqual()
    {
        var expected = new ToolRequest("a", "flag", """{"on":true,"note":null}""");
        var actual = new ToolRequest("b", "flag", "{\n \"note\": null,\n \"on\": true\n}");

        ToolRequestMatch.Matches(expected, actual).Should().BeTrue();
    }

    [TestMethod]
    public void Matches_ScientificNotationEqualToPlainNumber_AreEqual()
    {
        // 4e1 and 40 are the same value; both canonicalize through the decimal path.
        var expected = new ToolRequest("a", "amount", """{"value":4e1}""");
        var actual = new ToolRequest("b", "amount", """{"value":40}""");

        ToolRequestMatch.Matches(expected, actual).Should().BeTrue();
    }

    [TestMethod]
    public void Matches_NumberTooLargeForDecimal_ComparedByRawText()
    {
        // A number outside decimal's range keeps its raw text: still stable, and two identical raw
        // numbers match.
        var expected = new ToolRequest("a", "amount", """{"value":1e400}""");
        var actual = new ToolRequest("b", "amount", "{\n \"value\": 1e400\n}");

        ToolRequestMatch.Matches(expected, actual).Should().BeTrue();
    }

    [TestMethod]
    public void Matches_WhitespaceOnlyArguments_FallsBackToTrimmedStringEquality()
    {
        // Whitespace is not parseable JSON, so both sides fall back to trimmed string comparison.
        var expected = new ToolRequest("a", "noop", "   ");
        var actual = new ToolRequest("b", "noop", "");

        ToolRequestMatch.Matches(expected, actual).Should().BeTrue();
    }

    [TestMethod]
    public void Matches_JsonVersusPlainText_FallsBackAndIsNotEqual()
    {
        // One side canonicalizes, the other does not, so the comparison falls back to trimmed
        // string equality — which these two do not satisfy.
        var expected = new ToolRequest("a", "noop", """{"a":1}""");
        var actual = new ToolRequest("b", "noop", "not json");

        ToolRequestMatch.Matches(expected, actual).Should().BeFalse();
    }
}
