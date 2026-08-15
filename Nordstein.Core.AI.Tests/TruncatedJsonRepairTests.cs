using AwesomeAssertions;
using Nordstein.Core.AI.Serialization.Internal;

namespace Nordstein.Core.AI.Tests;

/// <summary>
/// Exercises <see cref="TruncatedJsonRepair.Candidates"/> directly: the empty / balanced short
/// circuits, the string-closing repair (including trailing backslash and mid-string escapes), the
/// container-closing repair, and the drop-incomplete-member second candidate.
/// </summary>
[TestClass]
public sealed class TruncatedJsonRepairTests
{
    [TestMethod]
    public void Candidates_EmptyString_ReturnsNothing()
        => TruncatedJsonRepair.Candidates(string.Empty).Should().BeEmpty();

    [TestMethod]
    public void Candidates_WhitespaceOnly_ReturnsNothing()
        => TruncatedJsonRepair.Candidates("   ").Should().BeEmpty();

    [TestMethod]
    public void Candidates_AlreadyBalancedJson_ReturnsNothing()
        => TruncatedJsonRepair.Candidates("""{"a":1}""").Should().BeEmpty();

    [TestMethod]
    public void Candidates_UnbalancedCloserOnly_ReturnsNothing()
        // A stray closing bracket with nothing open leaves the stack empty and the string closed,
        // so there is nothing to repair.
        => TruncatedJsonRepair.Candidates("]").Should().BeEmpty();

    [TestMethod]
    public void Candidates_UnterminatedStringValue_ClosesQuoteAndBraces()
    {
        List<string> candidates = TruncatedJsonRepair.Candidates("{\"a\":\"hello").ToList();

        candidates.Should().ContainSingle().Which.Should().Be("{\"a\":\"hello\"}");
    }

    [TestMethod]
    public void Candidates_TrailingBackslash_DropsBackslashBeforeClosingQuote()
    {
        // A lone trailing backslash would escape the quote the repair appends, so it is removed first.
        List<string> candidates = TruncatedJsonRepair.Candidates("{\"a\":\"foo\\").ToList();

        candidates.Should().ContainSingle().Which.Should().Be("{\"a\":\"foo\"}");
    }

    [TestMethod]
    public void Candidates_EscapedQuoteInsideString_IsPreservedThenClosed()
    {
        List<string> candidates = TruncatedJsonRepair.Candidates("{\"a\":\"foo\\\"bar").ToList();

        candidates.Should().ContainSingle().Which.Should().Be("{\"a\":\"foo\\\"bar\"}");
    }

    [TestMethod]
    public void Candidates_ClosedArrayInsideOpenObject_ClosesRemainingContainer()
    {
        List<string> candidates = TruncatedJsonRepair.Candidates("{\"a\":[1,2]").ToList();

        candidates.First().Should().Be("{\"a\":[1,2]}");
    }

    [TestMethod]
    public void Candidates_CutInsideMemberAfterAComma_SecondCandidateDropsIncompleteMember()
    {
        List<string> candidates = TruncatedJsonRepair.Candidates("{\"a\":1,\"b").ToList();

        candidates.Should().Equal("{\"a\":1,\"b\"}", "{\"a\":1}");
    }
}
