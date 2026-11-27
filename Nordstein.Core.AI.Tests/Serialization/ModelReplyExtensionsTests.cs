using AwesomeAssertions;
using Nordstein.Core.AI.Serialization;

namespace Nordstein.Core.AI.Tests.Serialization;

/// <summary>
/// Exercises <see cref="ModelReplyExtensions"/>: the one surrounding code fence (with and without a
/// language tag, closed and unclosed) and the leading reasoning block (closed, empty, orphan
/// close, unclosed), including the replies that must come back unchanged.
/// </summary>
[TestClass]
public sealed class ModelReplyExtensionsTests
{
    [TestMethod]
    public void StripCodeFence_Null_Throws()
    {
        Action act = () => NullReply().StripCodeFence();

        act.Should().Throw<ArgumentNullException>().WithParameterName("reply");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("plain text")]
    [DataRow("  {\"a\":1}  ")]
    [DataRow("text before ```\ncode\n```")]
    public void StripCodeFence_NotFenced_ReturnsTheReplyUnchanged(string reply)
        => reply.StripCodeFence().Should().Be(reply);

    [TestMethod]
    public void StripCodeFence_LanguageTag_DropsTheTagLineAndTheClosingFence()
        => "```json\n{\"a\":1}\n```".StripCodeFence().Should().Be("{\"a\":1}\n");

    [TestMethod]
    public void StripCodeFence_NoLanguageTag_DropsTheFenceLines()
        => "```\n{\"a\":1}\n```".StripCodeFence().Should().Be("{\"a\":1}\n");

    [TestMethod]
    public void StripCodeFence_SurroundingWhitespace_IsIgnoredAroundTheFence()
        => "  \n```json\n{\"a\":1}\n```  \n".StripCodeFence().Should().Be("{\"a\":1}\n");

    [TestMethod]
    public void StripCodeFence_NoClosingFence_DropsTheOpeningLineOnly()
        => "```json\n{\"a\":1}".StripCodeFence().Should().Be("{\"a\":1}");

    [TestMethod]
    public void StripCodeFence_NoClosingFenceAndTrailingWhitespace_TrimsTheEnd()
        => "```json\n{\"a\":1}\n  \n".StripCodeFence().Should().Be("{\"a\":1}");

    [TestMethod]
    public void StripCodeFence_CrLfLineBreaks_DropsTheTagLine()
        => "```json\r\n{\"a\":1}\r\n```".StripCodeFence().Should().Be("{\"a\":1}\r\n");

    [TestMethod]
    public void StripCodeFence_FenceAndBodyOnOneLine_DropsOnlyTheBackticks()
    {
        "```x```".StripCodeFence().Should().Be("x");
        "```json {\"a\":1}".StripCodeFence().Should().Be("json {\"a\":1}");
    }

    [TestMethod]
    public void StripCodeFence_OnlyAnOpeningFence_ReturnsEmpty()
        => "```".StripCodeFence().Should().BeEmpty();

    [TestMethod]
    public void StripCodeFence_EmptyFencedBlock_ReturnsEmpty()
        => "```\n```".StripCodeFence().Should().BeEmpty();

    [TestMethod]
    public void StripCodeFence_InnerFence_KeepsItAndRemovesOnlyTheOutermostClosing()
        => "```md\nfirst\n```\nsecond\n```".StripCodeFence().Should().Be("first\n```\nsecond\n");

    [TestMethod]
    public void TryStripReasoning_Null_Throws()
    {
        Action act = () => NullReply().TryStripReasoning(out _);

        act.Should().Throw<ArgumentNullException>().WithParameterName("reply");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("the answer")]
    [DataRow("  the answer  ")]
    public void TryStripReasoning_NoReasoning_ReturnsTrueWithTheReplyUnchanged(string reply)
    {
        bool closed = reply.TryStripReasoning(out string answer);

        closed.Should().BeTrue();
        answer.Should().Be(reply);
    }

    [TestMethod]
    public void TryStripReasoning_LeadingClosedBlock_DropsItWithTheWhitespaceAround()
    {
        bool closed = "  \n<think>\nsome reasoning\n</think>\n\nthe answer\n".TryStripReasoning(out string answer);

        closed.Should().BeTrue();
        answer.Should().Be("the answer\n");
    }

    [TestMethod]
    public void TryStripReasoning_EmptyClosedBlock_DropsIt()
    {
        bool closed = "<think></think>the answer".TryStripReasoning(out string answer);

        closed.Should().BeTrue();
        answer.Should().Be("the answer");
    }

    [TestMethod]
    public void TryStripReasoning_ClosedBlockOnly_ReturnsEmptyAnswer()
    {
        bool closed = "<think>nothing else</think>  ".TryStripReasoning(out string answer);

        closed.Should().BeTrue();
        answer.Should().BeEmpty();
    }

    [TestMethod]
    public void TryStripReasoning_SecondBlock_IsKept()
    {
        bool closed = "<think>a</think>b<think>c</think>d".TryStripReasoning(out string answer);

        closed.Should().BeTrue();
        answer.Should().Be("b<think>c</think>d");
    }

    [TestMethod]
    public void TryStripReasoning_OrphanClose_DropsEverythingUpToTheFirstClose()
    {
        bool closed = "reasoning one</think>  reasoning two</think>the answer".TryStripReasoning(out string answer);

        closed.Should().BeTrue();
        answer.Should().Be("reasoning two</think>the answer");
    }

    [TestMethod]
    public void TryStripReasoning_OrphanCloseAtStart_ReturnsTheRestTrimmed()
    {
        bool closed = "</think>\n\nthe answer".TryStripReasoning(out string answer);

        closed.Should().BeTrue();
        answer.Should().Be("the answer");
    }

    [TestMethod]
    public void TryStripReasoning_CloseBeforeALaterOpen_ReturnsTheReplyUnchanged()
    {
        const string Reply = "x</think>y<think>z";

        bool closed = Reply.TryStripReasoning(out string answer);

        closed.Should().BeTrue();
        answer.Should().Be(Reply);
    }

    [TestMethod]
    public void TryStripReasoning_LateBlock_IsNotLeadingAndStaysInTheReply()
    {
        const string Reply = "{\"a\":1}\n<think>late</think>";

        bool closed = Reply.TryStripReasoning(out string answer);

        closed.Should().BeTrue();
        answer.Should().Be(Reply);
    }

    [TestMethod]
    public void TryStripReasoning_Unclosed_ReturnsFalseAndNoAnswer()
    {
        bool closed = "<think>\nstill thinking SECRET".TryStripReasoning(out string answer);

        closed.Should().BeFalse();
        answer.Should().BeEmpty("the reasoning must never be read as the answer");
    }

    [TestMethod]
    public void TryStripReasoning_OpenTagOnly_ReturnsFalse()
        => "<think>".TryStripReasoning(out _).Should().BeFalse();

    // An unassigned element of a string array: a null the nullable analysis does not flag, which the
    // extension methods must still reject.
    private static string NullReply() => (new string[1])[0];
}
